using HorizonNET.Api.Services.Calendar;
using HorizonNET.Shared.Transfer.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace HorizonNET.Api.Controllers;

// Anbieterneutrale Kalender-Endpunkte – gelten für den gerade verbundenen Anbieter
// (Google oder Nextcloud). Verbinden selbst ist anbieterspezifisch: api/google/connect
// bzw. api/caldav/connection.
[ApiController]
[Route("api/calendar")]
public class CalendarController(CalendarSyncService calendar) : ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> Status() => Ok(await calendar.GetStatusAsync());

    // Termine des verbundenen Kalenders im angegebenen Zeitraum (read-only).
    [HttpGet("events")]
    public async Task<IActionResult> Events([FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to)
        => Ok(await calendar.GetEventsAsync(from.UtcDateTime, to.UtcDateTime));

    // Vorlaufzeit der Erinnerung an den gespiegelten Terminen. Wirkt erst beim nächsten
    // Spiegeln eines Tasks – bestehende Termine werden nicht rückwirkend angefasst.
    [HttpGet("reminder")]
    public async Task<IActionResult> GetReminder() =>
        Ok(new CalendarReminderDto(await calendar.GetReminderMinutesAsync()));

    [HttpPut("reminder")]
    public async Task<IActionResult> SetReminder([FromBody] CalendarReminderDto dto)
    {
        if (dto.Minutes is < 0 or > 40320) return BadRequest(); // Google erlaubt max. 4 Wochen

        await calendar.SetReminderMinutesAsync(dto.Minutes);
        return NoContent();
    }

    // Trennt den verbundenen Anbieter. Die Termine bleiben im Kalender stehen.
    [HttpDelete]
    public async Task<IActionResult> Disconnect()
    {
        await calendar.DisconnectAsync();
        return NoContent();
    }
}
