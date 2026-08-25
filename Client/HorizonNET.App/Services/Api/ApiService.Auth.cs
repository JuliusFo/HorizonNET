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
            HttpStatusCode.OK           => LoginOutcome.Success,
            HttpStatusCode.Unauthorized => LoginOutcome.WrongCredentials,
            HttpStatusCode.Locked       => LoginOutcome.LockedOut,
            _                           => LoginOutcome.Error
        };
    }

    public Task<bool> LogoutAsync() => PostAsync("api/auth/logout");

    // Wer bin ich laut Cookie? null = keine (gültige) Sitzung.
    public async Task<AuthUserDto?> GetCurrentUserAsync()
    {
        try { return await http.GetFromJsonAsync<AuthUserDto>("api/auth/me"); }
        catch { return null; }
    }
}
