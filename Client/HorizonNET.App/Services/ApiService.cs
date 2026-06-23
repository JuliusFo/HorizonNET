using System.Net.Http.Json;
using HorizonNET.Shared.Transfer.DTOs;

namespace HorizonNET.App.Services;

// Kapselt alle HTTP-Aufrufe an die HorizonNET-API
public class ApiService(HttpClient http)
{
    // ── Projekte ────────────────────────────────────────────────────────────

    public Task<List<ProjectResponseDto>?> GetProjectsAsync() =>
        http.GetFromJsonAsync<List<ProjectResponseDto>>("api/projects");

    public Task<ProjectResponseDto?> GetProjectAsync(int id) =>
        http.GetFromJsonAsync<ProjectResponseDto>($"api/projects/{id}");

    public async Task<ProjectResponseDto?> CreateProjectAsync(ProjectCreateDto dto)
    {
        var response = await http.PostAsJsonAsync("api/projects", dto);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<ProjectResponseDto>()
            : null;
    }

    public async Task<ProjectResponseDto?> UpdateProjectAsync(int id, ProjectUpdateDto dto)
    {
        var response = await http.PutAsJsonAsync($"api/projects/{id}", dto);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<ProjectResponseDto>()
            : null;
    }

    public async Task<bool> DeleteProjectAsync(int id)
    {
        var response = await http.DeleteAsync($"api/projects/{id}");
        return response.IsSuccessStatusCode;
    }

    // ── Tasks ────────────────────────────────────────────────────────────────

    public Task<List<TaskResponseDto>?> GetTasksAsync() =>
        http.GetFromJsonAsync<List<TaskResponseDto>>("api/tasks");

    public Task<List<TaskResponseDto>?> GetTasksByProjectAsync(int projectId) =>
        http.GetFromJsonAsync<List<TaskResponseDto>>($"api/tasks/project/{projectId}");

    public Task<List<TaskResponseDto>?> GetInboxTasksAsync() =>
        http.GetFromJsonAsync<List<TaskResponseDto>>("api/tasks/inbox");

    public Task<TaskResponseDto?> GetTaskAsync(int id) =>
        http.GetFromJsonAsync<TaskResponseDto>($"api/tasks/{id}");

    public async Task<TaskResponseDto?> CreateTaskAsync(TaskCreateDto dto)
    {
        var response = await http.PostAsJsonAsync("api/tasks", dto);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<TaskResponseDto>()
            : null;
    }

    public async Task<TaskResponseDto?> UpdateTaskAsync(int id, TaskUpdateDto dto)
    {
        var response = await http.PutAsJsonAsync($"api/tasks/{id}", dto);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<TaskResponseDto>()
            : null;
    }

    public async Task<bool> DeleteTaskAsync(int id)
    {
        var response = await http.DeleteAsync($"api/tasks/{id}");
        return response.IsSuccessStatusCode;
    }
}
