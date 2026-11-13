using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using HorizonNET.Shared.Transfer.DTOs;

namespace HorizonNET.Api.Services.Calendar;

// Fassade über den Kalender-Anbietern (Google oder Nextcloud/CalDAV). Controller und
// Seriengenerator reden nur mit ihr; sie leitet an den gerade verbundenen Anbieter weiter
// oder tut nichts, wenn keiner verbunden ist.
//
// Entweder-oder: Verbinden des einen Anbieters trennt den anderen (ConnectedAsync). Weil
// die gespeicherten Event-IDs nur im alten Kalender gelten, werden sie dabei verworfen und
// alle geplanten Tasks ab heute im neuen Kalender neu angelegt. Die alten Termine bleiben
// im alten Kalender stehen.
public class CalendarSyncService(
    GoogleCalendarService google,
    CalDavCalendarService caldav,
    ITaskRepository tasks,
    IAppSettingRepository settings,
    IServiceScopeFactory scopeFactory,
    ILogger<CalendarSyncService> logger)
{
    // Vorlaufzeit der Erinnerung am gespiegelten Termin, in Minuten. Fehlt der Wert,
    // gilt "keine Erinnerung". Bis Version mit Nextcloud-Anbindung hieß der Schlüssel
    // "google.reminderMinutes" – die Migration AddCalDavConnection zieht ihn um.
    public const string ReminderSettingKey = "calendar.reminderMinutes";

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
        var active = await GetActiveProviderAsync();
        if (active is null) return;

        await active.SyncTaskAsync(task, await GetReminderMinutesAsync());
    }

    // Löscht das Event eines (bereits lokal entfernten) Tasks (best-effort).
    public async Task DeleteTaskEventAsync(string eventId)
    {
        var active = await GetActiveProviderAsync();
        if (active is null) return;

        await active.DeleteEventAsync(eventId);
    }

    // Nach dem Speichern einer neuen Verbindung aufzurufen: trennt alle anderen Anbieter,
    // verwirft die alten Event-IDs und spiegelt die geplanten Tasks im Hintergrund neu.
    public async Task ConnectedAsync(ICalendarProvider connected)
    {
        foreach (var p in Providers.Where(p => p != connected))
            await p.DisconnectAsync();

        await tasks.ClearAllCalendarEventIdsAsync();
        StartResyncInBackground();
    }

    // Trennt den verbundenen Anbieter. Die Event-IDs werden verworfen: Sie gälten nach
    // einem erneuten Verbinden (womöglich mit einem anderen Kalender) ohnehin nicht mehr.
    public async Task DisconnectAsync()
    {
        foreach (var p in Providers)
            await p.DisconnectAsync();

        await tasks.ClearAllCalendarEventIdsAsync();
    }

    // Alle geplanten Tasks ab heute neu spiegeln. Sequentiell, damit der Kalender-Server
    // nicht mit parallelen Anfragen überrannt wird.
    public async Task<int> ResyncAllAsync(CancellationToken ct = default)
    {
        var active = await GetActiveProviderAsync();
        if (active is null) return 0;

        var reminder = await GetReminderMinutesAsync();
        var scheduled = await tasks.GetScheduledFromAsync(DateTime.Today);
        foreach (var task in scheduled)
        {
            ct.ThrowIfCancellationRequested();
            await active.SyncTaskAsync(task, reminder);
        }
        return scheduled.Count;
    }

    // Läuft in einem eigenen Scope, weil der Request-Scope (DbContext) nach der Antwort
    // entsorgt wird. Hunderte Termine dauern einige Sekunden – das soll weder den
    // OAuth-Redirect noch den Verbinden-Button aufhalten.
    private void StartResyncInBackground() =>
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<CalendarSyncService>();
                var count = await svc.ResyncAllAsync();
                logger.LogInformation("Kalender-Neuabgleich: {Count} Tasks gespiegelt.", count);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Kalender-Neuabgleich fehlgeschlagen.");
            }
        });

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
