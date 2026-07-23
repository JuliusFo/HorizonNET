using System.Reflection;
using HorizonNET.Shared.Transfer.DTOs;
using Microsoft.JSInterop;

namespace HorizonNET.App.Services;

// Kennt die Client-Version (aus der eigenen Assembly) und holt die API-Version (Phase 9c).
// Warnt beim App-Start einmalig per Toast, wenn beide auseinanderlaufen – typischer Fall:
// ein alt im Browser gecachter WASM-Client gegen eine frisch deployte API.
public class VersionState(ApiService api, ToastService toast, IJSRuntime js)
{
    // InformationalVersion trägt "X.Y.Z+{commit}" (aus der zentralen Directory.Build.props).
    public string ClientVersion { get; } =
        typeof(VersionState).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(VersionState).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    public AppVersionDto? ApiVersion { get; private set; }

    private bool apiLoaded;
    private bool warned;

    // Nur die Nummer ohne "+{commit}" – für die Anzeige.
    public string ClientVersionShort => Short(ClientVersion);
    public string? ApiVersionShort => ApiVersion is null ? null : Short(ApiVersion.Version);

    // Holt die API-Version höchstens einmal und merkt sie sich (für die Anzeige in den
    // Einstellungen und den Versatz-Vergleich).
    public async Task EnsureApiLoadedAsync()
    {
        if (apiLoaded) return;
        ApiVersion = await api.GetApiVersionAsync();
        apiLoaded = true;
    }

    // Beim App-Start aufrufen: warnt einmalig bei Versatz mit „Neu laden"-Toast.
    public async Task CheckForMismatchAsync()
    {
        await EnsureApiLoadedAsync();
        if (warned || ApiVersion is null) return;
        warned = true;

        if (IsMismatch(ClientVersion, ApiVersion.Version))
            toast.ShowUpdate(
                "Neue Version verfügbar. Bitte neu laden.",
                () => js.InvokeVoidAsync("location.reload").AsTask());
    }

    // Verglichen wird NUR die Nummer (X.Y.Z), nicht der angehängte Commit.
    //
    // Vorher lief der Vergleich über die volle InformationalVersion. Das schlug beim
    // Entwickeln dauerhaft an, weil Client und API praktisch immer aus unterschiedlichen
    // Builds derselben Version stammen – und Neuladen konnte daran nichts ändern, denn
    // beide Werte sind einkompiliert. Der Toast war damit ein Dauerzustand statt eines
    // Signals. Anschlagen soll er beim echten Versionssprung.
    //
    // Bewusst statisch und ohne Abhängigkeiten: So liegt die Regel an einer Stelle und
    // ist prüfbar, ohne den ganzen Dienst zu bauen.
    public static bool IsMismatch(string clientVersion, string? apiVersion) =>
        apiVersion is not null
        && !string.Equals(Short(clientVersion), Short(apiVersion), StringComparison.OrdinalIgnoreCase);

    private static string Short(string version)
    {
        var plus = version.IndexOf('+');
        return plus < 0 ? version : version[..plus];
    }
}
