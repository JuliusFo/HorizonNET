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

    // ── Haus-Helfer für die Aufrufe ────────────────────────────────────────────
    // Fast jede Methode dieser Klasse macht dasselbe: absetzen, Erfolg prüfen, Antwort
    // lesen oder null/false liefern. Das stand rund sechzigmal ausgeschrieben da.
    //
    // Absichtlich dieselben Namen wie bei HttpClient (Post/Put/Delete): Innerhalb dieser
    // Klasse ist immer der Helfer gemeint, und der Unterschied ist an der Signatur
    // ablesbar – der Helfer nimmt eine URL, kein HttpContent, und liefert bereits das
    // fertige Ergebnis statt einer HttpResponseMessage.
    //
    // GET-Aufrufe bleiben bewusst direkt bei http.GetFromJsonAsync: Sie sind schon
    // Einzeiler, und der ApiErrorHandler macht aus einem Fehler dort ohnehin ein null.

    private async Task<T?> PostAsync<T>(string url, object body)
    {
        var response = await http.PostAsJsonAsync(url, body);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<T>()
            : default;
    }

    // Ohne Rumpf – für Endpunkte, die allein aus der URL bestehen (Timer starten/stoppen).
    private async Task<T?> PostAsync<T>(string url)
    {
        var response = await http.PostAsync(url, null);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<T>()
            : default;
    }

    private async Task<T?> PutAsync<T>(string url, object body)
    {
        var response = await http.PutAsJsonAsync(url, body);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<T>()
            : default;
    }

    // Die drei folgenden interessiert nur, OB es geklappt hat – die Antwort ist leer
    // (NoContent) oder wird nicht gebraucht.

    private async Task<bool> PostAsync(string url) =>
        (await http.PostAsync(url, null)).IsSuccessStatusCode;

    private async Task<bool> PutAsync(string url, object body) =>
        (await http.PutAsJsonAsync(url, body)).IsSuccessStatusCode;

    private async Task<bool> DeleteAsync(string url) =>
        (await http.DeleteAsync(url)).IsSuccessStatusCode;

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

    public Task<bool> PurgeTrashItemAsync(string type, int id) =>
        DeleteAsync($"api/trash/{type}/{id}");

    public Task<bool> EmptyTrashAsync() => DeleteAsync("api/trash");

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

    public Task<bool> DisconnectGoogleAsync() => DeleteAsync("api/google");

    // Vorlaufzeit der Erinnerung am gespiegelten Termin (null = keine). Fehler werden
    // geschluckt wie bei den übrigen Google-Aufrufen – die Einstellungsseite bleibt nutzbar.
    public async Task<int?> GetGoogleReminderAsync()
    {
        try { return (await http.GetFromJsonAsync<GoogleReminderDto>("api/google/reminder"))?.Minutes; }
        catch { return null; }
    }

    public async Task<bool> SetGoogleReminderAsync(int? minutes)
    {
        try
        {
            var antwort = await http.PutAsJsonAsync("api/google/reminder", new GoogleReminderDto(minutes));
            return antwort.IsSuccessStatusCode;
        }
        catch { return false; }
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
