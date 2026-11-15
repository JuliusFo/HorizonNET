using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using HorizonNET.Shared.Transfer.DTOs;

namespace HorizonNET.Api.Services.Calendar;

// Fassade über den Kalender-Anbietern (Google oder Nextcloud/CalDAV). Controller und
// Seriengenerator reden nur mit ihr; sie leitet an den gerade verbundenen Anbieter weiter
// oder tut nichts, wenn keiner verbunden ist.
//
// Entweder-oder: Verbinden des einen Anbieters trennt den anderen (ConnectedAsync).
//
// Event-IDs: Sie gelten nur beim Anbieter, der sie vergeben hat. Verworfen werden sie
// deshalb ausschließlich beim echten Anbieterwechsel (Google ↔ Nextcloud). Trennen und
// erneutes Verbinden desselben Anbieters – auch das erzwungene nach einem abgelaufenen
// Google-Token – setzt nahtlos fort: Die Termine werden aktualisiert statt neu angelegt.
// Welcher Anbieter die IDs vergeben hat, steht unter ProviderSettingKey.
//
// Alle schreibenden Wege laufen unter der Sperre des CalendarSyncCoordinator, siehe dort.
public class CalendarSyncService(
    GoogleCalendarService google,
    CalDavCalendarService caldav,
    ITaskRepository tasks,
    IAppSettingRepository settings,
    IServiceScopeFactory scopeFactory,
    CalendarSyncCoordinator coordinator,
    ILogger<CalendarSyncService> logger)
{
    // Vorlaufzeit der Erinnerung am gespiegelten Termin, in Minuten. Fehlt der Wert,
    // gilt "keine Erinnerung". Bis Version mit Nextcloud-Anbindung hieß der Schlüssel
    // "google.reminderMinutes" – die Migration AddCalDavConnection zieht ihn um.
    public const string ReminderSettingKey = "calendar.reminderMinutes";

    // Anbieter (Key), zu dem die gespeicherten CalendarEventIds gehören. Fehlt der Wert
    // (Bestand aus der Zeit vor Nextcloud), stammen vorhandene IDs von Google.
    public const string ProviderSettingKey = "calendar.eventIdProvider";

    private ICalendarProvider[] Providers => [google, caldav];

    // Der verbundene Anbieter oder null. Sollten durch einen Altbestand doch einmal
    // beide verbunden sein, gewinnt Google (Reihenfolge in Providers).
    public async Task<ICalendarProvider?> GetActiveProviderAsync()
    {
        foreach (var p in Providers)
            if (await p.IsConnectedAsync()) return p;
        return null;
    }

    public async Task<CalendarStatusDto> GetStatusAsync()
    {
        var active = await GetActiveProviderAsync();
        var status = active switch
        {
            GoogleCalendarService g => new CalendarStatusDto(g.Key, await g.GetAccountAsync(), null),
            CalDavCalendarService c => await c.GetStatusAsync(),
            _ => new CalendarStatusDto(null, null, null)
        };
        return status with { GoogleAvailable = google.IsConfigured };
    }

    public async Task<IReadOnlyList<CalendarEventDto>> GetEventsAsync(DateTime fromUtc, DateTime toUtc)
    {
        var active = await GetActiveProviderAsync();
        if (active is null) return [];

        var ownIds = await tasks.GetCalendarEventIdsAsync();
        return await active.GetEventsAsync(fromUtc, toUtc, ownIds);
    }

    // Spiegelt einen Task in den verbundenen Kalender (best-effort, wirft nicht).
    public async Task SyncTaskAsync(TaskItem task)
    {
        using var _ = await coordinator.LockAsync();

        var active = await GetActiveProviderAsync();
        if (active is null) return;

        await SyncLockedAsync(active, task, await GetReminderMinutesAsync());
    }

    // Löscht das Event eines (bereits lokal entfernten) Tasks (best-effort).
    public async Task DeleteTaskEventAsync(string eventId)
    {
        using var _ = await coordinator.LockAsync();

        var active = await GetActiveProviderAsync();
        if (active is null) return;

        await active.DeleteEventAsync(eventId);
    }

    // Nach dem Speichern einer neuen Verbindung aufzurufen: trennt alle anderen Anbieter,
    // verwirft bei einem echten Anbieterwechsel die alten Event-IDs und spiegelt die
    // geplanten Tasks im Hintergrund (neu).
    public async Task ConnectedAsync(ICalendarProvider connected)
    {
        using (await coordinator.LockAsync())
        {
            // Über den Schlüssel vergleichen, nicht über die Referenz: CalDavCalendarService ist
            // als typisierter HttpClient registriert und damit transient – der Controller hält
            // eine andere Instanz als diese Fassade. Ein Referenzvergleich hielt die eigene
            // Nextcloud-Instanz für "den anderen Anbieter" und löschte die eben gespeicherte
            // Verbindung gleich wieder.
            foreach (var p in Providers.Where(p => p.Key != connected.Key))
                await p.DisconnectAsync();

            var previous = await settings.GetAsync(ProviderSettingKey);
            if (string.IsNullOrEmpty(previous)) previous = CalendarProviders.Google;

            if (previous != connected.Key)
            {
                await tasks.ClearAllCalendarEventIdsAsync();
                await settings.SetAsync(ProviderSettingKey, connected.Key);
            }
        }

        StartResyncInBackground();
    }

    // Trennt den verbundenen Anbieter. Die Event-IDs bleiben: Wer denselben Anbieter
    // wieder verbindet, soll seine Termine fortgeschrieben bekommen statt verdoppelt.
    public async Task DisconnectAsync()
    {
        using var _ = await coordinator.LockAsync();

        foreach (var p in Providers)
            await p.DisconnectAsync();
    }

    // Alle geplanten Tasks ab heute neu spiegeln. Sequentiell, damit der Kalender-Server
    // nicht mit parallelen Anfragen überrannt wird.
    //
    // Jeder Task wird erst unter der Sperre frisch gelesen: Die Liste ist beim Abarbeiten
    // längst veraltet, und ein zwischendurch geänderter Titel soll nicht vom Abgleich mit
    // dem alten Stand überschrieben werden. Wechselt währenddessen der Anbieter oder
    // startet ein neuerer Abgleich, hört dieser hier auf.
    public async Task<int> ResyncAllAsync(int? generation = null, CancellationToken ct = default)
    {
        var started = await GetActiveProviderAsync();
        if (started is null) return 0;

        var reminder = await GetReminderMinutesAsync();
        var ids = await tasks.GetScheduledIdsFromAsync(DateTime.Today);
        var synced = 0;

        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            if (generation is int g && !coordinator.IsCurrentResync(g)) break;

            using (await coordinator.LockAsync(ct))
            {
                var active = await GetActiveProviderAsync();
                if (active?.Key != started.Key) break;

                var task = await tasks.GetForCalendarSyncAsync(id);
                if (task is null) continue; // inzwischen gelöscht

                await SyncLockedAsync(active, task, reminder);
                synced++;
            }
        }
        return synced;
    }

    // Unter der Sperre: Die Event-ID frisch aus der Datenbank nehmen. Der übergebene Task
    // kann sie veraltet tragen, wenn der Abgleich sie gerade in einem anderen Scope gesetzt hat.
    private async Task SyncLockedAsync(ICalendarProvider active, TaskItem task, int? reminder)
    {
        task.CalendarEventId = await tasks.GetCalendarEventIdAsync(task.Id);
        await active.SyncTaskAsync(task, reminder);
    }

    // Läuft in einem eigenen Scope, weil der Request-Scope (DbContext) nach der Antwort
    // entsorgt wird. Hunderte Termine dauern einige Sekunden – das soll weder den
    // OAuth-Redirect noch den Verbinden-Button aufhalten.
    private void StartResyncInBackground()
    {
        var generation = coordinator.NextResyncGeneration();
        var ct = coordinator.Stopping;

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<CalendarSyncService>();
                var count = await svc.ResyncAllAsync(generation, ct);
                logger.LogInformation("Kalender-Neuabgleich: {Count} Tasks gespiegelt.", count);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Herunterfahren – beim nächsten Verbinden oder Speichern geht es weiter.
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Kalender-Neuabgleich fehlgeschlagen.");
            }
        }, CancellationToken.None);
    }

    // ── Erinnerungen ─────────────────────────────────────────────────────────────

    // Ohne gespeicherten Wert bewusst KEINE Erinnerung: Früher setzte die App am Google-
    // Termin gar kein Reminders-Feld, wodurch die Standard-Erinnerung des Kalenders griff –
    // genau die Benachrichtigung, die auf dem Handy auch bei längst erledigten Tasks ankam.
    public async Task<int?> GetReminderMinutesAsync()
    {
        var gespeichert = await settings.GetAsync(ReminderSettingKey);
        return int.TryParse(gespeichert, out var minuten) && minuten >= 0 ? minuten : null;
    }

    public async Task SetReminderMinutesAsync(int? minutes) =>
        await settings.SetAsync(ReminderSettingKey, minutes?.ToString() ?? string.Empty);
}
