using HorizonNET.Domain.Interfaces;
using HorizonNET.Shared.Transfer.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace HorizonNET.Api.Controllers;

// Papierkorb: listet soft-gelöschte Einträge aller Typen und löscht sie endgültig.
// Das Wiederherstellen läuft bewusst NICHT hier, sondern über die bestehenden
// {controller}/{id}/restore-Endpunkte – so bleibt insbesondere der Google-Sync des
// TasksController (SyncTaskAsync nach Restore) erhalten.
[ApiController]
[Route("api/[controller]")]
public class TrashController(
    IWorkspaceRepository workspaces,
    IProjectRepository projects,
    ITaskRepository tasks,
    INoteRepository notes,
    INoteFolderRepository noteFolders,
    IDailyTaskRepository dailyTasks) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var items = new List<TrashItemDto>();

        items.AddRange((await workspaces.GetDeletedAsync()).Select(w => new TrashItemDto(
            TrashItemTypes.Workspace, w.Id, w.Name, null, w.DeletedAt!.Value)));

        items.AddRange((await projects.GetDeletedAsync()).Select(p => new TrashItemDto(
            TrashItemTypes.Project, p.Id, p.Name, p.Workspace?.Name, p.DeletedAt!.Value)));

        items.AddRange((await tasks.GetDeletedAsync()).Select(t => new TrashItemDto(
            TrashItemTypes.Task, t.Id, t.Title, t.Project?.Name, t.DeletedAt!.Value)));

        items.AddRange((await notes.GetDeletedAsync()).Select(n => new TrashItemDto(
            TrashItemTypes.Note, n.Id, n.Title, n.TaskItem?.Title ?? n.Project?.Name, n.DeletedAt!.Value)));

        items.AddRange((await dailyTasks.GetDeletedAsync()).Select(d => new TrashItemDto(
            TrashItemTypes.DailyTask, d.Id, d.Title, d.Project?.Name, d.DeletedAt!.Value)));

        items.AddRange((await noteFolders.GetDeletedAsync()).Select(f => new TrashItemDto(
            TrashItemTypes.NoteFolder, f.Id, f.Name, null, f.DeletedAt!.Value)));

        // Zuletzt gelöscht zuerst, typübergreifend.
        return Ok(items.OrderByDescending(i => i.DeletedAt));
    }

    [HttpDelete("{type}/{id:int}")]
    public async Task<IActionResult> Purge(string type, int id)
    {
        var purged = await PurgeByTypeAsync(type, id);
        if (purged is null) return BadRequest($"Unbekannter Typ: {type}");
        if (!purged.Value) return NotFound();
        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> EmptyAll()
    {
        // Projekte zuerst: räumt ihre Tasks gleich mit weg, sodass die anschließende
        // Task-Wurzelliste nur noch eigenständig gelöschte Tasks enthält.
        foreach (var p in await projects.GetDeletedAsync())
            await projects.PurgeAsync(p.Id);
        foreach (var t in await tasks.GetDeletedAsync())
            await tasks.PurgeAsync(t.Id);
        foreach (var n in await notes.GetDeletedAsync())
            await notes.PurgeAsync(n.Id);
        foreach (var d in await dailyTasks.GetDeletedAsync())
            await dailyTasks.PurgeAsync(d.Id);
        foreach (var f in await noteFolders.GetDeletedAsync())
            await noteFolders.PurgeAsync(f.Id);
        foreach (var w in await workspaces.GetDeletedAsync())
            await workspaces.PurgeAsync(w.Id);

        return NoContent();
    }

    // null = unbekannter Typ; sonst Ergebnis des jeweiligen PurgeAsync.
    private async Task<bool?> PurgeByTypeAsync(string type, int id) => type switch
    {
        TrashItemTypes.Workspace => await workspaces.PurgeAsync(id),
        TrashItemTypes.Project   => await projects.PurgeAsync(id),
        TrashItemTypes.Task      => await tasks.PurgeAsync(id),
        TrashItemTypes.Note      => await notes.PurgeAsync(id),
        TrashItemTypes.DailyTask => await dailyTasks.PurgeAsync(id),
        TrashItemTypes.NoteFolder => await noteFolders.PurgeAsync(id),
        _ => null
    };
}
