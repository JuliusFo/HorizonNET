using System.Net.Http.Json;
using HorizonNET.Shared.Transfer.DTOs;

namespace HorizonNET.App.Services;

// Kalender-Sync (Google oder Nextcloud). Status, Termine und Erinnerung sind
// anbieterneutral (api/calendar); verbunden wird Google per OAuth-Redirect
// (api/google/connect), Nextcloud über api/caldav.
public partial class ApiService
{
    public Task<CalendarStatusDto?> GetCalendarStatusAsync() =>
        http.GetFromJsonAsync<CalendarStatusDto>("api/calendar/status");

    public Task<bool> DisconnectCalendarAsync() => DeleteAsync("api/calendar");

    // Vorlaufzeit der Erinnerung am gespiegelten Termin (null = keine). Fehler werden
    // geschluckt – die Einstellungsseite bleibt nutzbar.
    public async Task<int?> GetCalendarReminderAsync()
    {
        try { return (await http.GetFromJsonAsync<CalendarReminderDto>("api/calendar/reminder"))?.Minutes; }
        catch { return null; }
    }

    public async Task<bool> SetCalendarReminderAsync(int? minutes)
    {
        try
        {
            var antwort = await http.PutAsJsonAsync("api/calendar/reminder", new CalendarReminderDto(minutes));
            return antwort.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // Holt die Termine des verbundenen Kalenders. Fehler (z. B. nicht verbunden oder
    // Server nicht erreichbar) werden geschluckt, damit der Kalender trotzdem funktioniert.
    public async Task<List<CalendarEventDto>> GetCalendarEventsAsync(DateTime fromUtc, DateTime toUtc)
    {
        try
        {
            var from = new DateTimeOffset(fromUtc, TimeSpan.Zero).ToString("o");
            var to = new DateTimeOffset(toUtc, TimeSpan.Zero).ToString("o");
            var url = $"api/calendar/events?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}";
            return await http.GetFromJsonAsync<List<CalendarEventDto>>(url) ?? [];
        }
        catch
        {
            return [];
        }
    }

    // ── Nextcloud / CalDAV ─────────────────────────────────────────────────────

    public Task<CalDavDiscoverResultDto?> DiscoverCalDavCalendarsAsync(CalDavCredentialsDto dto) =>
        PostAsync<CalDavDiscoverResultDto>("api/caldav/discover", dto);

    public Task<CalDavConnectResultDto?> ConnectCalDavAsync(CalDavConnectDto dto) =>
        PutAsync<CalDavConnectResultDto>("api/caldav/connection", dto);
}
