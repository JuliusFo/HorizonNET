using System.Text.Json;
using Microsoft.JSInterop;

namespace HorizonNET.App.Services;

// Was die Wochenabschluss-Seite sich merkt: Empfänger, Arbeitsbereich-Auswahl, Anrede
// und Signatur. Wie SettingsState nur im localStorage dieses Browsers – der Server
// braucht nichts davon, die Mail entsteht komplett im Client.
public class WeeklyMailSettings(IJSRuntime js)
{
    private const string StorageKey = "weeklyMail.settings";

    public string To { get; set; } = string.Empty;
    public string Cc { get; set; } = string.Empty;
    public List<int> WorkspaceIds { get; set; } = [];
    public bool WithoutWorkspace { get; set; }
    public string Greeting { get; set; } = "Hallo zusammen,";
    public string Signature { get; set; } = "Viele Grüße";

    private bool loaded;

    public async Task EnsureLoadedAsync()
    {
        if (loaded) return;
        loaded = true;

        var json = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
        if (string.IsNullOrEmpty(json)) return;

        try
        {
            var stored = JsonSerializer.Deserialize<Stored>(json);
            if (stored is null) return;
            To = stored.To ?? string.Empty;
            Cc = stored.Cc ?? string.Empty;
            WorkspaceIds = stored.WorkspaceIds ?? [];
            WithoutWorkspace = stored.WithoutWorkspace;
            Greeting = stored.Greeting ?? Greeting;
            Signature = stored.Signature ?? Signature;
        }
        catch (JsonException)
        {
            // Kaputter Eintrag: mit den Standardwerten weiter, beim nächsten Speichern überschrieben.
        }
    }

    public Task SaveAsync() =>
        js.InvokeVoidAsync("localStorage.setItem", StorageKey,
            JsonSerializer.Serialize(new Stored(To, Cc, WorkspaceIds, WithoutWorkspace, Greeting, Signature))).AsTask();

    private record Stored(
        string? To, string? Cc, List<int>? WorkspaceIds, bool WithoutWorkspace, string? Greeting, string? Signature);
}
