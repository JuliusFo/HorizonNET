using HorizonNET.Domain.Entities;
using HorizonNET.Shared.Transfer.DTOs;

namespace HorizonNET.Api.Services.Calendar;

// Ein Kalender-Anbieter für den Einweg-Sync (App → Kalender) und die read-only-Anzeige
// fremder Termine. Es ist immer höchstens einer verbunden – die Auswahl trifft
// CalendarSyncService, die Aufrufer (Controller, Seriengenerator) sehen nur die Fassade.
//
// Alle schreibenden Methoden sind best-effort: Ein Kalender-Problem darf das lokale
// Speichern nie blockieren, deshalb werfen sie nicht.
public interface ICalendarProvider
{
    // Maschinenlesbarer Name, landet im Status-DTO: "google" oder "nextcloud".
    string Key { get; }

    Task<bool> IsConnectedAsync();

    // Trennt nur die Verbindung; Termine im Kalender bleiben stehen.
    Task DisconnectAsync();

    // Termine im (UTC-)Zeitraum, ohne die von der App selbst gespiegelten (ownEventIds).
    // Leere Liste, wenn nicht verbunden oder der Anbieter nicht erreichbar ist.
    Task<IReadOnlyList<CalendarEventDto>> GetEventsAsync(
        DateTime fromUtc, DateTime toUtc, IReadOnlySet<string> ownEventIds);

    // Spiegelt einen Task (anlegen/aktualisieren/bei fehlendem Datum entfernen) und
    // pflegt dabei TaskItem.CalendarEventId.
    Task SyncTaskAsync(TaskItem task, int? defaultReminderMinutes);

    Task DeleteEventAsync(string eventId);
}
