using System.Net.Http.Json;
using HorizonNET.Shared.Transfer.DTOs;
using HorizonNET.Shared.Transfer.Enums;

namespace HorizonNET.App.Services;

// Arbeitsbereiche und Projekte.
public partial class ApiService
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

    public async Task<bool> RestoreWorkspaceAsync(int id)
    {
        var response = await http.PostAsync($"api/workspaces/{id}/restore", null);
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

    public async Task<bool> RestoreProjectAsync(int id)
    {
        var response = await http.PostAsync($"api/projects/{id}/restore", null);
        return response.IsSuccessStatusCode;
    }

}
