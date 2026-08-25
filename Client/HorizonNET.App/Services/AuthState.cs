namespace HorizonNET.App.Services;

// Angemeldet-Zustand der App. Das MainLayout zeigt die Login-Maske (LoginGate),
// solange hier niemand angemeldet ist – Seiten und Navigation existieren dann
// gar nicht erst.
public class AuthState(ApiService api)
{
    // Erst nach der ersten /api/auth/me-Antwort bekannt; bis dahin zeigt das
    // Layout weder App noch Login-Maske (vermeidet Aufblitzen der falschen Ansicht).
    public bool Initialized { get; private set; }

    public string? Username { get; private set; }

    public bool IsLoggedIn => Username is not null;

    public event Action? Changed;

    // Beim App-Start: Besteht noch eine gültige Cookie-Sitzung?
    public async Task InitializeAsync()
    {
        Username = (await api.GetCurrentUserAsync())?.Username;
        Initialized = true;
        Changed?.Invoke();
    }

    public async Task<LoginOutcome> LoginAsync(string username, string password)
    {
        var outcome = await api.LoginAsync(username, password);
        if (outcome == LoginOutcome.Success)
        {
            Username = username;
            Changed?.Invoke();
        }
        return outcome;
    }

    public async Task LogoutAsync()
    {
        await api.LogoutAsync();
        Username = null;
        Changed?.Invoke();
    }

    // Vom ApiErrorHandler bei 401 außerhalb der Auth-Endpunkte gerufen:
    // Die Sitzung ist serverseitig abgelaufen, zurück zur Login-Maske.
    public void NotifySessionExpired()
    {
        if (Username is null) return;
        Username = null;
        Changed?.Invoke();
    }
}
