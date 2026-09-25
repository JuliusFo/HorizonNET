using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;

namespace HorizonNET.Api.Services;

// Materialisiert Terminserien zu echten Terminen und hält Google nach. Zwei Aufrufer:
// der Hosted Service (App-Start + täglich) und der TaskSeriesController (nach Anlegen/
// Ändern, damit der Nutzer die Termine sofort sieht, nicht erst morgen früh).
//
// Der Google-Sync liegt bewusst hier und nicht im Repository – wie beim TasksController:
// Repositories bleiben ohne Netz, und der Sync ist best-effort.
public class TaskSeriesGenerator(
    ITaskSeriesRepository series,
    ITaskRepository tasks,
    GoogleCalendarService google,
    ILogger<TaskSeriesGenerator> logger)
{
    // Rollierender Horizont: Wie weit im Voraus Termine entstehen. Acht Wochen reichen
    // für die Planung, ohne den Kalender mit einem Jahr Volleyball zuzupflastern.
    public static readonly TimeSpan Horizon = TimeSpan.FromDays(7 * 8);

    // Legt alle fehlenden Termine bis zum Horizont an und spiegelt sie nach Google.
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var from = DateOnly.FromDateTime(DateTime.Now);
        var to   = from.AddDays(Horizon.Days);

        var created = await series.MaterializeAsync(from, to);
        foreach (var task in created)
        {
            ct.ThrowIfCancellationRequested();
            await google.SyncTaskAsync(task);
        }

        if (created.Count > 0)
            logger.LogInformation("Terminserien: {Count} Termine bis {To} angelegt.", created.Count, to);

        return created.Count;
    }

    // Google nach einer Serien-Änderung nachziehen: geänderte/zurückgeholte Termine
    // spiegeln, entfernte Termine aus dem Google-Kalender nehmen.
    public async Task SyncAsync(TaskSeriesChange change)
    {
        foreach (var task in change.Upserted)
            await google.SyncTaskAsync(task);

        foreach (var task in change.Removed)
        {
            if (string.IsNullOrEmpty(task.GoogleEventId)) continue;
            await google.DeleteTaskEventAsync(task.GoogleEventId);
            // Verknüpfung lösen: Kommt der Termin per Undo zurück, legt der Sync ein
            // frisches Event an, statt ein gelöschtes aktualisieren zu wollen.
            await tasks.SetGoogleEventIdAsync(task.Id, null);
        }
    }
}
