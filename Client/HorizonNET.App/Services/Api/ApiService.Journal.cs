using System.Net.Http.Json;
using HorizonNET.Shared.Transfer.DTOs;

namespace HorizonNET.App.Services;

// Tagebuch, Stimmungen und Vorlagen.
public partial class ApiService
{
    // ── Journal ──────────────────────────────────────────────────────────────────

    // Liefert null, wenn für den Tag noch nichts geschrieben wurde. Das ist beim
    // Blättern der Normalfall und kein Fehler: Die API antwortet mit 404, und der
    // ApiErrorHandler macht daraus bewusst ein stilles null ohne Fehler-Toast.
    public Task<JournalEntryResponseDto?> GetJournalEntryAsync(DateOnly date) =>
        http.GetFromJsonAsync<JournalEntryResponseDto>($"api/journal/{date:yyyy-MM-dd}");

    // Anlegen und Ändern in einem: Der Schlüssel ist der Tag, nicht eine Id.
    public Task<JournalEntryResponseDto?> SaveJournalEntryAsync(
        DateOnly date, JournalEntryUpsertDto dto) =>
        PutAsync<JournalEntryResponseDto>($"api/journal/{date:yyyy-MM-dd}", dto);

    // Sucht in Tagestext, Überschrift und Stimmungsnotizen. Bewusst ein eigener
    // Endpunkt – die globale Palette (Strg+K) kennt das Journal nicht.
    public Task<List<JournalSearchHitDto>?> SearchJournalAsync(
        string? query, string? tag, DateOnly? from, DateOnly? to)
    {
        var parameters = new List<string>();
        if (!string.IsNullOrWhiteSpace(query)) parameters.Add($"q={Uri.EscapeDataString(query)}");
        if (!string.IsNullOrWhiteSpace(tag)) parameters.Add($"tag={Uri.EscapeDataString(tag)}");
        if (from is not null) parameters.Add($"from={from:yyyy-MM-dd}");
        if (to is not null) parameters.Add($"to={to:yyyy-MM-dd}");

        return http.GetFromJsonAsync<List<JournalSearchHitDto>>(
            $"api/journal/search?{string.Join('&', parameters)}");
    }

    public Task<List<JournalTagDto>?> GetJournalTagsAsync() =>
        http.GetFromJsonAsync<List<JournalTagDto>>("api/journal/tags");

    // Zurückliegende Einträge zum selben Kalendertag. Ohne Text – gelesen wird im
    // Journal, also hinter der Sperre.
    public Task<List<OnThisDayDto>?> GetOnThisDayAsync() =>
        http.GetFromJsonAsync<List<OnThisDayDto>>("api/journal/onthisday");

    // ── Journal-Vorlagen ─────────────────────────────────────────────────────────

    public Task<List<JournalTemplateResponseDto>?> GetJournalTemplatesAsync() =>
        http.GetFromJsonAsync<List<JournalTemplateResponseDto>>("api/journaltemplates");

    public Task<JournalTemplateResponseDto?> CreateJournalTemplateAsync(JournalTemplateCreateDto dto) =>
        PostAsync<JournalTemplateResponseDto>("api/journaltemplates", dto);

    public Task<JournalTemplateResponseDto?> UpdateJournalTemplateAsync(
        int id, JournalTemplateUpdateDto dto) =>
        PutAsync<JournalTemplateResponseDto>($"api/journaltemplates/{id}", dto);

    public Task<bool> DeleteJournalTemplateAsync(int id) =>
        DeleteAsync($"api/journaltemplates/{id}");

    // Was die App über den Tag ohnehin weiß (Tasks, Dailies, Zeit, Sport). Wird bei
    // jedem Öffnen frisch gelesen und nie in den Eintrag kopiert.
    public Task<JournalContextDto?> GetJournalContextAsync(DateOnly date) =>
        http.GetFromJsonAsync<JournalContextDto>($"api/journal/{date:yyyy-MM-dd}/context");

    // Tagesliste OHNE Inhalt – für Verlauf, Heatmap und Rückblick. Der Text ist der mit
    // Abstand größte Teil eines Eintrags und wird von keiner dieser Ansichten gezeigt.
    public Task<List<JournalListItemDto>?> GetJournalRangeAsync(DateOnly from, DateOnly to) =>
        http.GetFromJsonAsync<List<JournalListItemDto>>(
            $"api/journal?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");

    // Stimmungen: mehrere pro Tag. Existiert der Tageseintrag noch nicht, legt ihn der
    // Server leer an – eine Stimmung festzuhalten setzt kein Schreiben voraus.
    public Task<MoodResponseDto?> AddMoodAsync(DateOnly date, MoodCreateDto dto) =>
        PostAsync<MoodResponseDto>($"api/journal/{date:yyyy-MM-dd}/moods", dto);

    public Task<MoodResponseDto?> UpdateMoodAsync(int id, MoodUpdateDto dto) =>
        PutAsync<MoodResponseDto>($"api/journal/moods/{id}", dto);

    public Task<bool> DeleteMoodAsync(int id) =>
        DeleteAsync($"api/journal/moods/{id}");

    // Gelöschte Journal-Einträge. Bewusst ein eigener Endpunkt statt des globalen
    // Papierkorbs: Tagebuch-Einträge sollen dort nicht mitgelistet werden.
    public Task<List<JournalDeletedItemDto>?> GetDeletedJournalEntriesAsync() =>
        http.GetFromJsonAsync<List<JournalDeletedItemDto>>("api/journal/deleted");

    public Task<bool> DeleteJournalEntryAsync(int id) =>
        DeleteAsync($"api/journal/{id}");

    public Task<bool> RestoreJournalEntryAsync(int id) =>
        PostAsync($"api/journal/{id}/restore");

    // Endgültig – nicht umkehrbar, nimmt die Stimmungen des Tages mit.
    public Task<bool> PurgeJournalEntryAsync(int id) =>
        DeleteAsync($"api/journal/{id}/purge");
}
