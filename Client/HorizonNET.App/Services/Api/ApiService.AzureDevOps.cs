using System.Net.Http.Json;
using HorizonNET.Shared.Transfer.DTOs;

namespace HorizonNET.App.Services;

// Azure-DevOps-Anbindung: Verbindung, Projekt-Verknüpfung, Work-Item-Import.
// Fachliche Fehler (falscher Token …) kommen als 200 mit Error-Feld – siehe AzureDevOpsController.
public partial class ApiService
{
    public Task<AzureDevOpsStatusDto?> GetAzureDevOpsStatusAsync() =>
        http.GetFromJsonAsync<AzureDevOpsStatusDto>("api/azuredevops/status");

    public Task<AzureDevOpsConnectResultDto?> ConnectAzureDevOpsAsync(AzureDevOpsConnectDto dto) =>
        PutAsync<AzureDevOpsConnectResultDto>("api/azuredevops/connection", dto);

    public Task<bool> DisconnectAzureDevOpsAsync() => DeleteAsync("api/azuredevops");

    public Task<List<string>?> GetAzureDevOpsProjectsAsync() =>
        http.GetFromJsonAsync<List<string>>("api/azuredevops/projects");

    public Task<ProjectResponseDto?> LinkAzureDevOpsProjectAsync(int projectId, string? azureDevOpsProject) =>
        PutAsync<ProjectResponseDto>($"api/azuredevops/projects/{projectId}/link",
            new AzureDevOpsProjectLinkDto(azureDevOpsProject));

    public Task<AzureDevOpsWorkItemListDto?> GetAzureDevOpsWorkItemsAsync(int projectId, bool includeClosed) =>
        http.GetFromJsonAsync<AzureDevOpsWorkItemListDto>(
            $"api/azuredevops/workitems?projectId={projectId}&includeClosed={(includeClosed ? "true" : "false")}");

    public async Task<AzureDevOpsImportResultDto?> ImportAzureDevOpsWorkItemsAsync(AzureDevOpsImportDto dto)
    {
        var result = await PostAsync<AzureDevOpsImportResultDto>("api/azuredevops/import", dto);
        if (result is { Imported: > 0 }) await NotifyTaskChangedAsync();
        return result;
    }
}
