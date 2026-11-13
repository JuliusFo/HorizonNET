using HorizonNET.Api.Services;
using HorizonNET.Domain.Interfaces;
using HorizonNET.Shared.Transfer.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace HorizonNET.Api.Controllers;

// Azure-DevOps-Anbindung: Verbindung per PAT, Projekt-Verknüpfung und Work-Item-Import.
//
// Fehler von Azure DevOps (falscher Token, Projekt weg …) kommen als 200 mit Error-Feld
// zurück, nie als 401: Ein 401 hielte der Client für eine abgelaufene HorizonNET-Sitzung
// und schickte den Nutzer zur Anmeldung.
[ApiController]
[Route("api/azuredevops")]
public class AzureDevOpsController(
    AzureDevOpsService azure,
    IProjectRepository projects,
    GoogleCalendarService google) : ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> Status() => Ok(await azure.GetStatusAsync());

    [HttpPut("connection")]
    public async Task<IActionResult> Connect([FromBody] AzureDevOpsConnectDto dto)
    {
        try
        {
            var status = await azure.ConnectAsync(dto.OrganizationUrl, dto.PersonalAccessToken);
            return Ok(new AzureDevOpsConnectResultDto(true, null, status));
        }
        catch (AzureDevOpsException ex)
        {
            return Ok(new AzureDevOpsConnectResultDto(false, ex.Message, null));
        }
    }

    [HttpDelete]
    public async Task<IActionResult> Disconnect()
    {
        await azure.DisconnectAsync();
        return NoContent();
    }

    // Team-Projekte der Organisation – für die Verknüpfung im Projekt-Dialog.
    // Bei Fehler eine leere Liste; der Dialog zeigt dann "keine Projekte gefunden".
    [HttpGet("projects")]
    public async Task<IActionResult> Projects()
    {
        try { return Ok(await azure.GetProjectsAsync()); }
        catch (AzureDevOpsException) { return Ok(new List<string>()); }
    }

    // Teil-Update am Horizon-Projekt: nur die Verknüpfung.
    [HttpPut("projects/{projectId:int}/link")]
    public async Task<IActionResult> LinkProject(int projectId, [FromBody] AzureDevOpsProjectLinkDto dto)
    {
        if (dto.AzureDevOpsProject is { Length: > 256 }) return BadRequest();
        var updated = await projects.SetAzureDevOpsProjectAsync(projectId, dto.AzureDevOpsProject);
        if (updated is null) return NotFound();
        return Ok(ProjectsController.ToDto(updated));
    }

    [HttpGet("workitems")]
    public async Task<IActionResult> WorkItems([FromQuery] int projectId, [FromQuery] bool includeClosed = false)
    {
        try
        {
            return Ok(new AzureDevOpsWorkItemListDto(await azure.GetMyWorkItemsAsync(projectId, includeClosed), null));
        }
        catch (AzureDevOpsException ex)
        {
            return Ok(new AzureDevOpsWorkItemListDto([], ex.Message));
        }
    }

    [HttpPost("import")]
    public async Task<IActionResult> Import([FromBody] AzureDevOpsImportDto dto)
    {
        if (dto.WorkItemIds.Count is 0 or > 500) return BadRequest();

        try
        {
            var (created, skipped) = await azure.ImportAsync(dto.ProjectId, dto.WorkItemIds);
            // Wie beim normalen Anlegen: best-effort in Google spiegeln (ohne Fälligkeit
            // passiert dort nichts – bleibt aber derselbe Weg wie in TasksController.Create).
            foreach (var task in created)
                await google.SyncTaskAsync(task);
            return Ok(new AzureDevOpsImportResultDto(created.Count, skipped, null));
        }
        catch (AzureDevOpsException ex)
        {
            return Ok(new AzureDevOpsImportResultDto(0, 0, ex.Message));
        }
    }
}
