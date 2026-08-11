using System.Net.Http.Json;
using HorizonNET.Shared.Transfer.DTOs;

namespace HorizonNET.App.Services;

// Wiederkehrende Tagesaufgaben.
public partial class ApiService
{
    // ── Daily Tasks ──────────────────────────────────────────────────────────────

    public Task<List<DailyTaskResponseDto>?> GetDailyTasksAsync() =>
        http.GetFromJsonAsync<List<DailyTaskResponseDto>>("api/dailytasks");

    public Task<List<DailyTaskResponseDto>?> GetDailyTasksTodayAsync() =>
        http.GetFromJsonAsync<List<DailyTaskResponseDto>>("api/dailytasks/today");

    public Task<DailyTaskResponseDto?> CreateDailyTaskAsync(DailyTaskCreateDto dto) =>
        PostAsync<DailyTaskResponseDto>("api/dailytasks", dto);

    public Task<DailyTaskResponseDto?> UpdateDailyTaskAsync(int id, DailyTaskUpdateDto dto) =>
        PutAsync<DailyTaskResponseDto>($"api/dailytasks/{id}", dto);

    public Task<bool> DeleteDailyTaskAsync(int id) =>
        DeleteAsync($"api/dailytasks/{id}");

    public Task<bool> RestoreDailyTaskAsync(int id) =>
        PostAsync($"api/dailytasks/{id}/restore");

    public Task<bool> ReorderDailyTasksAsync(List<int> orderedIds) =>
        PutAsync("api/dailytasks/reorder", orderedIds);

    // Häkchen für einen Tag setzen/entfernen (Datum als yyyy-MM-dd; null = heute serverseitig).
    // Setzen und Entfernen liegen auf derselben URL, nur das Verb unterscheidet sich.
    public Task<bool> SetDailyTaskCompletionAsync(int id, DateOnly date, bool completed)
    {
        var url = $"api/dailytasks/{id}/complete?date={date:yyyy-MM-dd}";
        return completed ? PostAsync(url) : DeleteAsync(url);
    }
}
