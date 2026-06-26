using HorizonNET.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace HorizonNET.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GoogleController(GoogleCalendarService google, IConfiguration config) : ControllerBase
{
    // Startet den OAuth-Flow: Weiterleitung zur Google-Zustimmungsseite.
    [HttpGet("connect")]
    public IActionResult Connect() => Redirect(google.BuildAuthorizationUrl(RedirectUri()));

    // Rückleitung von Google: Code gegen Tokens tauschen, dann zurück zur Einstellungsseite.
    [HttpGet("callback")]
    public async Task<IActionResult> Callback(string? code, string? error)
    {
        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
            return Redirect(ClientReturnUrl("error"));

        try
        {
            await google.HandleCallbackAsync(code, RedirectUri());
            return Redirect(ClientReturnUrl("connected"));
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

    [HttpDelete]
    public async Task<IActionResult> Disconnect()
    {
        await google.DisconnectAsync();
        return NoContent();
    }

    // Muss exakt der in der Google Cloud Console registrierten Redirect-URI entsprechen.
    private string RedirectUri() => $"{Request.Scheme}://{Request.Host}/api/google/callback";

    // Zurück zur Einstellungsseite des Clients (separater Origin), mit Status-Flag.
    private string ClientReturnUrl(string status)
    {
        var clientUrl = (config["Cors:AllowedOrigin"] ?? string.Empty).TrimEnd('/');
        return $"{clientUrl}/settings?google={status}";
    }
}
