using HorizonNET.Api.Services.Calendar;
using HorizonNET.Shared.Transfer.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace HorizonNET.Api.Controllers;

// Nextcloud-/CalDAV-Anbindung: Kalender suchen und verbinden.
//
// Fehler des CalDAV-Servers (falsches Passwort, falsche URL …) kommen als 200 mit
// Error-Feld zurück, nie als 401: Ein 401 hielte der Client für eine abgelaufene
// HorizonNET-Sitzung und schickte den Nutzer zur Anmeldung.
[ApiController]
[Route("api/caldav")]
public class CalDavController(CalDavCalendarService caldav, CalendarSyncService calendar) : ControllerBase
{
    [HttpPost("discover")]
    public async Task<IActionResult> Discover([FromBody] CalDavCredentialsDto dto)
    {
        try
        {
            var calendars = await caldav.DiscoverAsync(dto.ServerUrl, dto.Username, dto.AppPassword);
            return Ok(new CalDavDiscoverResultDto(true, null, calendars));
        }
        catch (CalDavException ex)
        {
            return Ok(new CalDavDiscoverResultDto(false, ex.Message, []));
        }
    }

    [HttpPut("connection")]
    public async Task<IActionResult> Connect([FromBody] CalDavConnectDto dto)
    {
        try
        {
            await caldav.ConnectAsync(dto);
        }
        catch (CalDavException ex)
        {
            return Ok(new CalDavConnectResultDto(false, ex.Message, null));
        }

        // Entweder-oder: Google trennen, alte Event-IDs verwerfen, geplante Tasks im
        // Hintergrund in den gewählten Kalender spiegeln.
        await calendar.ConnectedAsync(caldav);
        return Ok(new CalDavConnectResultDto(true, null, await calendar.GetStatusAsync()));
    }
}
