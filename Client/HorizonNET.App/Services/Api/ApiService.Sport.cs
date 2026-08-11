using System.Net.Http.Json;
using HorizonNET.Shared.Transfer.DTOs;

namespace HorizonNET.App.Services;

// Übungen, Sätze und Körpergewicht.
public partial class ApiService
{
    // ── Sport: Übungen ─────────────────────────────────────────────────────────

    public Task<List<ExerciseResponseDto>?> GetExercisesAsync() =>
        http.GetFromJsonAsync<List<ExerciseResponseDto>>("api/exercises");

    public Task<ExerciseResponseDto?> GetExerciseAsync(int id) =>
        http.GetFromJsonAsync<ExerciseResponseDto>($"api/exercises/{id}");

    public Task<ExerciseResponseDto?> CreateExerciseAsync(ExerciseCreateDto dto) =>
        PostAsync<ExerciseResponseDto>("api/exercises", dto);

    public Task<ExerciseResponseDto?> UpdateExerciseAsync(int id, ExerciseUpdateDto dto) =>
        PutAsync<ExerciseResponseDto>($"api/exercises/{id}", dto);

    public Task<bool> ReorderExercisesAsync(List<int> orderedIds) =>
        PutAsync("api/exercises/reorder", orderedIds);

    public Task<bool> DeleteExerciseAsync(int id) =>
        DeleteAsync($"api/exercises/{id}");

    public Task<bool> RestoreExerciseAsync(int id) =>
        PostAsync($"api/exercises/{id}/restore");

    // ── Sport: Sätze ───────────────────────────────────────────────────────────

    // 'to' ist exklusiv – ein ganzer Tag ist damit from=Tag, to=Tag+1.
    public Task<List<ExerciseSetResponseDto>?> GetExerciseSetsAsync(
        DateTime? from = null, DateTime? to = null, int? exerciseId = null)
    {
        var query = new List<string>();
        if (from is not null) query.Add($"from={from:yyyy-MM-ddTHH:mm:ss}");
        if (to is not null) query.Add($"to={to:yyyy-MM-ddTHH:mm:ss}");
        if (exerciseId is not null) query.Add($"exerciseId={exerciseId}");

        var url = "api/exercise-sets" + (query.Count > 0 ? "?" + string.Join("&", query) : "");
        return http.GetFromJsonAsync<List<ExerciseSetResponseDto>>(url);
    }

    // Diese beiden bleiben ausgeschrieben: Sie brauchen die Antwort AUCH im Fehlerfall.
    // Die typabhängigen Regeln ("Kraftübungen brauchen Wiederholungen und Gewicht") sind
    // für den Nutzer die eigentliche Auskunft und sollen nicht zu einem generischen
    // "hat nicht geklappt" verkommen – die Helfer oben werfen den Rumpf dagegen weg.
    public async Task<(ExerciseSetResponseDto? Set, string? Error)> CreateExerciseSetAsync(ExerciseSetCreateDto dto)
    {
        var response = await http.PostAsJsonAsync("api/exercise-sets", dto);
        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<ExerciseSetResponseDto>(), null);

        return (null, await ErrorTextAsync(response));
    }

    public async Task<(ExerciseSetResponseDto? Set, string? Error)> UpdateExerciseSetAsync(int id, ExerciseSetUpdateDto dto)
    {
        var response = await http.PutAsJsonAsync($"api/exercise-sets/{id}", dto);
        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<ExerciseSetResponseDto>(), null);

        return (null, await ErrorTextAsync(response));
    }

    public Task<bool> DeleteExerciseSetAsync(int id) =>
        DeleteAsync($"api/exercise-sets/{id}");

    public Task<bool> RestoreExerciseSetAsync(int id) =>
        PostAsync($"api/exercise-sets/{id}/restore");

    // ── Sport: Körpergewicht ───────────────────────────────────────────────────

    public Task<List<BodyWeightResponseDto>?> GetBodyWeightAsync(DateOnly? from = null, DateOnly? to = null)
    {
        var query = new List<string>();
        if (from is not null) query.Add($"from={from:yyyy-MM-dd}");
        if (to is not null) query.Add($"to={to:yyyy-MM-dd}");

        var url = "api/bodyweight" + (query.Count > 0 ? "?" + string.Join("&", query) : "");
        return http.GetFromJsonAsync<List<BodyWeightResponseDto>>(url);
    }

    public Task<BodyWeightResponseDto?> SetBodyWeightAsync(DateOnly measuredOn, double weightKg) =>
        PutAsync<BodyWeightResponseDto>("api/bodyweight", new BodyWeightSetDto(measuredOn, weightKg));

    public Task<bool> DeleteBodyWeightAsync(int id) =>
        DeleteAsync($"api/bodyweight/{id}");

    // Die API meldet Validierungsfehler als Klartext (BadRequest mit string).
    private static async Task<string> ErrorTextAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(text) ? "Speichern fehlgeschlagen." : text.Trim('"');
    }
}
