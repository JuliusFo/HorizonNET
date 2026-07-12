using System.Net.Http.Json;
using HorizonNET.Shared.Transfer.DTOs;

namespace HorizonNET.App.Services;

// Kapselt alle HTTP-Aufrufe an die HorizonNET-API
public class ApiService(HttpClient http)
{
    // ── Arbeitsbereiche ───────────────────────────────────────────────────────

    public Task<List<WorkspaceResponseDto>?> GetWorkspacesAsync() =>
        http.GetFromJsonAsync<List<WorkspaceResponseDto>>("api/workspaces");

    public Task<WorkspaceResponseDto?> GetWorkspaceAsync(int id) =>
        http.GetFromJsonAsync<WorkspaceResponseDto>($"api/workspaces/{id}");

    public async Task<WorkspaceResponseDto?> CreateWorkspaceAsync(WorkspaceCreateDto dto)
    {
        var response = await http.PostAsJsonAsync("api/workspaces", dto);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<WorkspaceResponseDto>()
            : null;
    }

    public async Task<WorkspaceResponseDto?> UpdateWorkspaceAsync(int id, WorkspaceUpdateDto dto)
    {
        var response = await http.PutAsJsonAsync($"api/workspaces/{id}", dto);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<WorkspaceResponseDto>()
            : null;
    }

    public async Task<bool> DeleteWorkspaceAsync(int id)
    {
        var response = await http.DeleteAsync($"api/workspaces/{id}");
        return response.IsSuccessStatusCode;
    }

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

    public async Task<bool> ReorderTasksAsync(TaskReorderDto dto)
    {
        var response = await http.PutAsJsonAsync("api/tasks/reorder", dto);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> ReorderSubTasksAsync(List<int> orderedTaskIds)
    {
        var response = await http.PutAsJsonAsync("api/tasks/reorder-subtasks", orderedTaskIds);
        return response.IsSuccessStatusCode;
    }

    // ── Notizen ────────────────────────────────────────────────────────────────

    public Task<List<NoteResponseDto>?> GetNotesAsync() =>
        http.GetFromJsonAsync<List<NoteResponseDto>>("api/notes");

    public Task<List<NoteResponseDto>?> GetNotesByTaskAsync(int taskId) =>
        http.GetFromJsonAsync<List<NoteResponseDto>>($"api/notes/task/{taskId}");

    public Task<List<NoteResponseDto>?> GetNotesByProjectAsync(int projectId) =>
        http.GetFromJsonAsync<List<NoteResponseDto>>($"api/notes/project/{projectId}");

    public async Task<NoteResponseDto?> CreateNoteAsync(NoteCreateDto dto)
    {
        var response = await http.PostAsJsonAsync("api/notes", dto);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<NoteResponseDto>()
            : null;
    }

    public async Task<NoteResponseDto?> UpdateNoteAsync(int id, NoteUpdateDto dto)
    {
        var response = await http.PutAsJsonAsync($"api/notes/{id}", dto);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<NoteResponseDto>()
            : null;
    }

    public async Task<bool> DeleteNoteAsync(int id)
    {
        var response = await http.DeleteAsync($"api/notes/{id}");
        return response.IsSuccessStatusCode;
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
