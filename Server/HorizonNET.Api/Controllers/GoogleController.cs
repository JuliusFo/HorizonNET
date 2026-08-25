using HorizonNET.Api.Services;
using HorizonNET.Shared.Transfer.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HorizonNET.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GoogleController(GoogleCalendarService google) : ControllerBase
{
    // Startet den OAuth-Flow: Weiterleitung zur Google-Zustimmungsseite.
    [HttpGet("connect")]
    public IActionResult Connect() => Redirect(google.BuildAuthorizationUrl(RedirectUri()));

    // Rückleitung von Google: Code gegen Tokens tauschen, dann zurück zur Einstellungsseite.
    // Anonym, weil der Browser hier per Redirect von accounts.google.com landet – wäre die
    // Sitzung genau dann abgelaufen, würde der ganze OAuth-Flow an einem 401 sterben.
    [HttpGet("callback")]
    [AllowAnonymous]
    public async Task<IActionResult> Callback(string? code, string? error)
    {
        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
            return Redirect(ClientReturnUrl("error"));

        try
        {
            await google.HandleCallbackAsync(code, RedirectUri());
            return Redirect(ClientReturnUrl("connected"));
        }
        catch (GoogleScopeNotGrantedException)
        {
            // Kalender-Berechtigung wurde nicht erteilt – eigener Status für eine
            // klare Meldung auf der Einstellungsseite.
            return Redirect(ClientReturnUrl("noscope"));
        }
        catch
        {
            return Redirect(ClientReturnUrl("error"));
        }
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status() => Ok(await google.GetStatusAsync());

    // Termine des primären Google-Kalenders im angegebenen Zeitraum (read-only).
    [HttpGet("events")]
    public async Task<IActionResult> Events([FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to)
        => Ok(await google.GetEventsAsync(from.UtcDateTime, to.UtcDateTime));

    // Vorlaufzeit der Erinnerung an den gespiegelten Terminen. Wirkt erst beim nächsten
    // Spiegeln eines Tasks – bestehende Google-Termine werden nicht rückwirkend angefasst.
    [HttpGet("reminder")]
    public async Task<IActionResult> GetReminder() =>
        Ok(new GoogleReminderDto(await google.GetReminderMinutesAsync()));

    [HttpPut("reminder")]
    public async Task<IActionResult> SetReminder([FromBody] GoogleReminderDto dto)
    {
        if (dto.Minutes is < 0 or > 40320) return BadRequest(); // Google erlaubt max. 4 Wochen

        await google.SetReminderMinutesAsync(dto.Minutes);
        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> Disconnect()
    {
        await google.DisconnectAsync();
        return NoContent();
    }

    // Muss exakt der in der Google Cloud Console registrierten Redirect-URI entsprechen.
    private string RedirectUri() => $"{Request.Scheme}://{Request.Host}/api/google/callback";

    // Zurück zur Einstellungsseite, mit Status-Flag. Seit dem Same-Origin-Hosting ist der
    // Client-Origin dieser Origin – wie bei RedirectUri() aus dem Request gebaut, damit es
    // je Umgebung (localhost, Domain hinter dem Tunnel) von selbst stimmt.
    private string ClientReturnUrl(string status) =>
        $"{Request.Scheme}://{Request.Host}/settings?google={status}";
}
