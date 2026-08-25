using System.Net;
using System.Net.Http.Json;
using HorizonNET.Shared.Transfer.DTOs;

namespace HorizonNET.App.Services;

// Ergebnis eines Login-Versuchs – die Login-Maske braucht mehr als ja/nein,
// um die richtige Meldung zu zeigen.
public enum LoginOutcome
{
    Success,
    WrongCredentials,
    LockedOut,
    RateLimited,
    Error
}

public partial class ApiService
{
    // ── Anmeldung ──────────────────────────────────────────────────────────────
    // Die Statuscodes wertet der Aufrufer selbst aus; der ApiErrorHandler lässt
    // Antworten der Auth-Endpunkte deshalb unangetastet durch.

    public async Task<LoginOutcome> LoginAsync(string username, string password)
    {
        var response = await http.PostAsJsonAsync("api/auth/login", new LoginRequestDto(username, password));
        return response.StatusCode switch
        {
            HttpStatusCode.OK              => LoginOutcome.Success,
            HttpStatusCode.Unauthorized    => LoginOutcome.WrongCredentials,
            HttpStatusCode.Locked          => LoginOutcome.LockedOut,
            HttpStatusCode.TooManyRequests => LoginOutcome.RateLimited,
            _                              => LoginOutcome.Error
        };
    }

    public Task<bool> LogoutAsync() => PostAsync("api/auth/logout");

    // null = geändert; sonst die anzeigbare Fehlermeldung aus der API
    // (falsches aktuelles Passwort, Passwort-Regeln).
    public async Task<string?> ChangePasswordAsync(string currentPassword, string newPassword)
    {
        var response = await http.PutAsJsonAsync("api/auth/password",
            new ChangePasswordDto(currentPassword, newPassword));

        if (response.IsSuccessStatusCode)
            return null;

        // BadRequest(string) kommt von MVC als text/plain (StringOutputFormatter),
        // deshalb kein JSON-Parsen.
        var text = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(text) ? "Passwort konnte nicht geändert werden." : text;
    }

    // Wer bin ich laut Cookie? null = keine (gültige) Sitzung.
    public async Task<AuthUserDto?> GetCurrentUserAsync()
    {
        try { return await http.GetFromJsonAsync<AuthUserDto>("api/auth/me"); }
        catch { return null; }
    }
}
