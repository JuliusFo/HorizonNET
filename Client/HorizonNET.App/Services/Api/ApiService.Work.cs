using System.Net.Http.Json;
using HorizonNET.Shared.Transfer.DTOs;

namespace HorizonNET.App.Services;

// Arbeitsbereiche und Projekte.
public partial class ApiService
{
    // ── Arbeitsbereiche ───────────────────────────────────────────────────────

    public Task<List<WorkspaceResponseDto>?> GetWorkspacesAsync() =>
        http.GetFromJsonAsync<List<WorkspaceResponseDto>>("api/workspaces");

    public Task<WorkspaceResponseDto?> GetWorkspaceAsync(int id) =>
        http.GetFromJsonAsync<WorkspaceResponseDto>($"api/workspaces/{id}");

    public Task<WorkspaceResponseDto?> CreateWorkspaceAsync(WorkspaceCreateDto dto) =>
        PostAsync<WorkspaceResponseDto>("api/workspaces", dto);

    public Task<WorkspaceResponseDto?> UpdateWorkspaceAsync(int id, WorkspaceUpdateDto dto) =>
        PutAsync<WorkspaceResponseDto>($"api/workspaces/{id}", dto);

    public Task<bool> DeleteWorkspaceAsync(int id) =>
        DeleteAsync($"api/workspaces/{id}");

    public Task<bool> RestoreWorkspaceAsync(int id) =>
        PostAsync($"api/workspaces/{id}/restore");

    // ── Projekte ────────────────────────────────────────────────────────────

    public Task<List<ProjectResponseDto>?> GetProjectsAsync() =>
        http.GetFromJsonAsync<List<ProjectResponseDto>>("api/projects");

    public Task<ProjectResponseDto?> GetProjectAsync(int id) =>
        http.GetFromJsonAsync<ProjectResponseDto>($"api/projects/{id}");

    public Task<ProjectResponseDto?> CreateProjectAsync(ProjectCreateDto dto) =>
        PostAsync<ProjectResponseDto>("api/projects", dto);

    public Task<ProjectResponseDto?> UpdateProjectAsync(int id, ProjectUpdateDto dto) =>
        PutAsync<ProjectResponseDto>($"api/projects/{id}", dto);

    public Task<bool> DeleteProjectAsync(int id) =>
        DeleteAsync($"api/projects/{id}");

    public Task<bool> RestoreProjectAsync(int id) =>
        PostAsync($"api/projects/{id}/restore");
}
