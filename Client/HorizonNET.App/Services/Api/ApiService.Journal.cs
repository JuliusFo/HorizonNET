using System.Net.Http.Json;
using HorizonNET.Shared.Transfer.DTOs;
using HorizonNET.Shared.Transfer.Enums;

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
    public async Task<JournalEntryResponseDto?> SaveJournalEntryAsync(
        DateOnly date, JournalEntryUpsertDto dto)
    {
        var response = await http.PutAsJsonAsync($"api/journal/{date:yyyy-MM-dd}", dto);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<JournalEntryResponseDto>()
            : null;
    }

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

    public async Task<JournalTemplateResponseDto?> CreateJournalTemplateAsync(JournalTemplateCreateDto dto)
    {
        var response = await http.PostAsJsonAsync("api/journaltemplates", dto);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<JournalTemplateResponseDto>()
            : null;
    }

    public async Task<JournalTemplateResponseDto?> UpdateJournalTemplateAsync(
        int id, JournalTemplateUpdateDto dto)
    {
        var response = await http.PutAsJsonAsync($"api/journaltemplates/{id}", dto);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<JournalTemplateResponseDto>()
            : null;
    }

    public async Task<bool> DeleteJournalTemplateAsync(int id)
    {
        var response = await http.DeleteAsync($"api/journaltemplates/{id}");
        return response.IsSuccessStatusCode;
    }

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
    public async Task<MoodResponseDto?> AddMoodAsync(DateOnly date, MoodCreateDto dto)
    {
        var response = await http.PostAsJsonAsync($"api/journal/{date:yyyy-MM-dd}/moods", dto);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<MoodResponseDto>()
            : null;
    }

    public async Task<MoodResponseDto?> UpdateMoodAsync(int id, MoodUpdateDto dto)
    {
        var response = await http.PutAsJsonAsync($"api/journal/moods/{id}", dto);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<MoodResponseDto>()
            : null;
    }

    public async Task<bool> DeleteMoodAsync(int id)
    {
        var response = await http.DeleteAsync($"api/journal/moods/{id}");
        return response.IsSuccessStatusCode;
    }

    // Gelöschte Journal-Einträge. Bewusst ein eigener Endpunkt statt des globalen
    // Papierkorbs: Tagebuch-Einträge sollen dort nicht mitgelistet werden.
    public Task<List<JournalDeletedItemDto>?> GetDeletedJournalEntriesAsync() =>
        http.GetFromJsonAsync<List<JournalDeletedItemDto>>("api/journal/deleted");

    public async Task<bool> DeleteJournalEntryAsync(int id)
    {
        var response = await http.DeleteAsync($"api/journal/{id}");
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> RestoreJournalEntryAsync(int id)
    {
        var response = await http.PostAsync($"api/journal/{id}/restore", null);
        return response.IsSuccessStatusCode;
    }

    // Endgültig – nicht umkehrbar, nimmt die Stimmungen des Tages mit.
    public async Task<bool> PurgeJournalEntryAsync(int id)
    {
        var response = await http.DeleteAsync($"api/journal/{id}/purge");
        return response.IsSuccessStatusCode;
    }

}
