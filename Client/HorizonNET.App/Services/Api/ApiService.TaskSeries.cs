using System.Net.Http.Json;
using HorizonNET.Shared.Transfer.DTOs;

namespace HorizonNET.App.Services;

// Terminserien (Schablonen, aus denen der Server Termine materialisiert).
public partial class ApiService
{
    // ── Terminserien ─────────────────────────────────────────────────────────────

    public Task<List<TaskSeriesResponseDto>?> GetTaskSeriesAsync() =>
        http.GetFromJsonAsync<List<TaskSeriesResponseDto>>("api/taskseries");

    public Task<TaskSeriesResponseDto?> GetTaskSeriesAsync(int id) =>
        http.GetFromJsonAsync<TaskSeriesResponseDto>($"api/taskseries/{id}");

    public Task<TaskSeriesResponseDto?> CreateTaskSeriesAsync(TaskSeriesCreateDto dto) =>
        PostAsync<TaskSeriesResponseDto>("api/taskseries", dto);

    public Task<TaskSeriesResponseDto?> UpdateTaskSeriesAsync(int id, TaskSeriesUpdateDto dto) =>
        PutAsync<TaskSeriesResponseDto>($"api/taskseries/{id}", dto);

    public Task<bool> DeleteTaskSeriesAsync(int id) =>
        DeleteAsync($"api/taskseries/{id}");

    public Task<bool> RestoreTaskSeriesAsync(int id) =>
        PostAsync($"api/taskseries/{id}/restore");
}
