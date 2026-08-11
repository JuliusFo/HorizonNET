using System.Net.Http.Json;
using HorizonNET.Shared.Transfer.DTOs;
using HorizonNET.Shared.Transfer.Enums;

namespace HorizonNET.App.Services;

// Wiederkehrende Tagesaufgaben.
public partial class ApiService
{
    // ── Daily Tasks ──────────────────────────────────────────────────────────────

    public Task<List<DailyTaskResponseDto>?> GetDailyTasksAsync() =>
        http.GetFromJsonAsync<List<DailyTaskResponseDto>>("api/dailytasks");

    public Task<List<DailyTaskResponseDto>?> GetDailyTasksTodayAsync() =>
        http.GetFromJsonAsync<List<DailyTaskResponseDto>>("api/dailytasks/today");

    public async Task<DailyTaskResponseDto?> CreateDailyTaskAsync(DailyTaskCreateDto dto)
    {
        var response = await http.PostAsJsonAsync("api/dailytasks", dto);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<DailyTaskResponseDto>()
            : null;
    }

    public async Task<DailyTaskResponseDto?> UpdateDailyTaskAsync(int id, DailyTaskUpdateDto dto)
    {
        var response = await http.PutAsJsonAsync($"api/dailytasks/{id}", dto);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<DailyTaskResponseDto>()
            : null;
    }

    public async Task<bool> DeleteDailyTaskAsync(int id)
    {
        var response = await http.DeleteAsync($"api/dailytasks/{id}");
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> RestoreDailyTaskAsync(int id)
    {
        var response = await http.PostAsync($"api/dailytasks/{id}/restore", null);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> ReorderDailyTasksAsync(List<int> orderedIds)
    {
        var response = await http.PutAsJsonAsync("api/dailytasks/reorder", orderedIds);
        return response.IsSuccessStatusCode;
    }

    // Häkchen für einen Tag setzen/entfernen (Datum als yyyy-MM-dd; null = heute serverseitig).
    public async Task<bool> SetDailyTaskCompletionAsync(int id, DateOnly date, bool completed)
    {
        var url = $"api/dailytasks/{id}/complete?date={date:yyyy-MM-dd}";
        var response = completed ? await http.PostAsync(url, null) : await http.DeleteAsync(url);
        return response.IsSuccessStatusCode;
    }

}
