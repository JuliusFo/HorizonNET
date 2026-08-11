using System.Net.Http.Json;
using HorizonNET.Shared.Transfer.DTOs;
using HorizonNET.Shared.Transfer.Enums;

namespace HorizonNET.App.Services;

// Übungen, Sätze und Körpergewicht.
public partial class ApiService
{
    // ── Sport: Übungen ─────────────────────────────────────────────────────────

    public Task<List<ExerciseResponseDto>?> GetExercisesAsync() =>
        http.GetFromJsonAsync<List<ExerciseResponseDto>>("api/exercises");

    public Task<ExerciseResponseDto?> GetExerciseAsync(int id) =>
        http.GetFromJsonAsync<ExerciseResponseDto>($"api/exercises/{id}");

    public async Task<ExerciseResponseDto?> CreateExerciseAsync(ExerciseCreateDto dto)
    {
        var response = await http.PostAsJsonAsync("api/exercises", dto);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<ExerciseResponseDto>()
            : null;
    }

    public async Task<ExerciseResponseDto?> UpdateExerciseAsync(int id, ExerciseUpdateDto dto)
    {
        var response = await http.PutAsJsonAsync($"api/exercises/{id}", dto);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<ExerciseResponseDto>()
            : null;
    }

    public async Task<bool> ReorderExercisesAsync(List<int> orderedIds)
    {
        var response = await http.PutAsJsonAsync("api/exercises/reorder", orderedIds);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteExerciseAsync(int id) =>
        (await http.DeleteAsync($"api/exercises/{id}")).IsSuccessStatusCode;

    public async Task<bool> RestoreExerciseAsync(int id) =>
        (await http.PostAsync($"api/exercises/{id}/restore", null)).IsSuccessStatusCode;

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

    // Liefert die Fehlermeldung der API mit: Die typabhängigen Regeln ("Kraftübungen
    // brauchen Wiederholungen und Gewicht") sind für den Nutzer die eigentliche Auskunft
    // und sollen nicht zu einem generischen "hat nicht geklappt" verkommen.
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

    public async Task<bool> DeleteExerciseSetAsync(int id) =>
        (await http.DeleteAsync($"api/exercise-sets/{id}")).IsSuccessStatusCode;

    public async Task<bool> RestoreExerciseSetAsync(int id) =>
        (await http.PostAsync($"api/exercise-sets/{id}/restore", null)).IsSuccessStatusCode;

    // ── Sport: Körpergewicht ───────────────────────────────────────────────────

    public Task<List<BodyWeightResponseDto>?> GetBodyWeightAsync(DateOnly? from = null, DateOnly? to = null)
    {
        var query = new List<string>();
        if (from is not null) query.Add($"from={from:yyyy-MM-dd}");
        if (to is not null) query.Add($"to={to:yyyy-MM-dd}");

        var url = "api/bodyweight" + (query.Count > 0 ? "?" + string.Join("&", query) : "");
        return http.GetFromJsonAsync<List<BodyWeightResponseDto>>(url);
    }

    public async Task<BodyWeightResponseDto?> SetBodyWeightAsync(DateOnly measuredOn, double weightKg)
    {
        var response = await http.PutAsJsonAsync("api/bodyweight", new BodyWeightSetDto(measuredOn, weightKg));
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<BodyWeightResponseDto>()
            : null;
    }

    public async Task<bool> DeleteBodyWeightAsync(int id) =>
        (await http.DeleteAsync($"api/bodyweight/{id}")).IsSuccessStatusCode;

    // Die API meldet Validierungsfehler als Klartext (BadRequest mit string).
    private static async Task<string> ErrorTextAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(text) ? "Speichern fehlgeschlagen." : text.Trim('"');
    }

}
