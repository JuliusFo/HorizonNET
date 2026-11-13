using HorizonNET.Api.Services;
using HorizonNET.Api.Services.Calendar;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HorizonNET.Api.Controllers;

// Nur der OAuth-Flow. Status, Termine, Erinnerung und Trennen laufen anbieterneutral
// über CalendarController (api/calendar).
[ApiController]
[Route("api/[controller]")]
public class GoogleController(GoogleCalendarService google, CalendarSyncService calendar) : ControllerBase
{
    // Startet den OAuth-Flow: Weiterleitung zur Google-Zustimmungsseite.
    [HttpGet("connect")]
    public IActionResult Connect()
    {
        if (!google.IsConfigured) return Redirect(ClientReturnUrl("error"));
        return Redirect(google.BuildAuthorizationUrl(RedirectUri()));
    }

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

        // Entweder-oder: eine bestehende Nextcloud-Verbindung trennen und die geplanten
        // Tasks im Hintergrund nach Google spiegeln.
        await calendar.ConnectedAsync(google);
        return Redirect(ClientReturnUrl("connected"));
    }

    // Muss exakt der in der Google Cloud Console registrierten Redirect-URI entsprechen.
    private string RedirectUri() => $"{Request.Scheme}://{Request.Host}/api/google/callback";

    // Zurück zur Einstellungsseite, mit Status-Flag. Seit dem Same-Origin-Hosting ist der
    // Client-Origin dieser Origin – wie bei RedirectUri() aus dem Request gebaut, damit es
    // je Umgebung (localhost, Domain hinter dem Tunnel) von selbst stimmt.
    private string ClientReturnUrl(string status) =>
        $"{Request.Scheme}://{Request.Host}/settings?google={status}";
}
