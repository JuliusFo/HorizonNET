using System.Net.Http.Json;
using HorizonNET.Shared.Transfer.DTOs;
using HorizonNET.Shared.Transfer.Enums;

namespace HorizonNET.App.Services;
// Kapselt alle HTTP-Aufrufe an die HorizonNET-API.
//
// Auf mehrere Dateien verteilt (ApiService.<Bereich>.cs im selben Ordner) – die Klasse
// hatte über 700 Zeilen und 100 Methoden. Hier stehen die geteilten Bausteine und die
// Abschnitte, die zu keinem Fachbereich gehören.
public partial class ApiService(HttpClient http)
{
    // Wird nach jeder Änderung an einem Task ausgelöst. Der TimerState hängt sich hier
    // ein: Ein Statuswechsel startet oder stoppt serverseitig die Zeiterfassung (auch
    // an einem anderen Task), und das erfährt der Client sonst nirgends zentral.
    public event Func<Task>? TaskChanged;

    private Task NotifyTaskChangedAsync() => TaskChanged?.Invoke() ?? Task.CompletedTask;

    // ── Globale Suche ──────────────────────────────────────────────────────────

    public Task<List<SearchHitDto>?> SearchAsync(string query) =>
        http.GetFromJsonAsync<List<SearchHitDto>>($"api/search?q={Uri.EscapeDataString(query)}");

    // ── Papierkorb ─────────────────────────────────────────────────────────────

    public Task<List<TrashItemDto>?> GetTrashAsync() =>
        http.GetFromJsonAsync<List<TrashItemDto>>("api/trash");

    // Stellt einen Papierkorb-Eintrag über den typspezifischen Restore-Endpunkt wieder
    // her – der Task-Restore spiegelt dabei serverseitig auch den Google-Termin neu.
    public Task<bool> RestoreTrashItemAsync(string type, int id) => type switch
    {
        TrashItemTypes.Workspace => RestoreWorkspaceAsync(id),
        TrashItemTypes.Project   => RestoreProjectAsync(id),
        TrashItemTypes.Task      => RestoreTaskAsync(id),
        TrashItemTypes.Note      => RestoreNoteAsync(id),
        TrashItemTypes.DailyTask => RestoreDailyTaskAsync(id),
        TrashItemTypes.NoteFolder => RestoreNoteFolderAsync(id),
        _ => Task.FromResult(false)
    };

    public async Task<bool> PurgeTrashItemAsync(string type, int id)
    {
        var response = await http.DeleteAsync($"api/trash/{type}/{id}");
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> EmptyTrashAsync()
    {
        var response = await http.DeleteAsync("api/trash");
        return response.IsSuccessStatusCode;
    }

    // ── Version ────────────────────────────────────────────────────────────────

    // Version der laufenden API. Fehler werden geschluckt (→ null), damit die
    // Versatz-Prüfung die App nie stört, wenn die API gerade nicht antwortet.
    public async Task<AppVersionDto?> GetApiVersionAsync()
    {
        try { return await http.GetFromJsonAsync<AppVersionDto>("api/version"); }
        catch { return null; }
    }

    // ── Google-Kalender ────────────────────────────────────────────────────────

    public Task<GoogleStatusDto?> GetGoogleStatusAsync() =>
        http.GetFromJsonAsync<GoogleStatusDto>("api/google/status");

    public async Task<bool> DisconnectGoogleAsync()
    {
        var response = await http.DeleteAsync("api/google");
        return response.IsSuccessStatusCode;
    }

    // Holt die Google-Termine eines Zeitraums. Fehler (z. B. nicht verbunden oder
    // Google nicht erreichbar) werden geschluckt, damit der Kalender trotzdem funktioniert.
    public async Task<List<GoogleEventDto>> GetGoogleEventsAsync(DateTime fromUtc, DateTime toUtc)
    {
        try
        {
            var from = new DateTimeOffset(fromUtc, TimeSpan.Zero).ToString("o");
            var to = new DateTimeOffset(toUtc, TimeSpan.Zero).ToString("o");
            var url = $"api/google/events?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}";
            return await http.GetFromJsonAsync<List<GoogleEventDto>>(url) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
