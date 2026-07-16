using HorizonNET.Domain.Interfaces;
using HorizonNET.Shared.Transfer.DTOs;
using HorizonNET.Shared.Transfer.Enums;
using Microsoft.AspNetCore.Mvc;

namespace HorizonNET.Api.Controllers;

// Globale Suche über Tasks, Projekte und Notizen (Kommandopalette, Strg+K).
// Soft-gelöschte Einträge sind durch die globalen Query-Filter automatisch ausgeschlossen.
[ApiController]
[Route("api/[controller]")]
public class SearchController(
    ITaskRepository tasks,
    IProjectRepository projects,
    INoteRepository notes) : ControllerBase
{
    // Obergrenze je Kategorie – die Palette zeigt ohnehin nur die besten Treffer.
    private const int PerTypeLimit = 5;

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string? q)
    {
        var query = q?.Trim();
        if (string.IsNullOrEmpty(query))
            return Ok(Array.Empty<SearchHitDto>());

        var taskHits    = await tasks.SearchAsync(query, PerTypeLimit);
        var projectHits = await projects.SearchAsync(query, PerTypeLimit);
        var noteHits    = await notes.SearchAsync(query, PerTypeLimit);

        // Reihenfolge = Anzeigereihenfolge in der Palette.
        var results = projectHits
            .Select(p => new SearchHitDto(
                SearchHitTypes.Project, p.Id, p.Name, p.Workspace?.Name))
            .Concat(taskHits.Select(t => new SearchHitDto(
                SearchHitTypes.Task, t.Id, t.Title, t.Project?.Name)))
            .Concat(noteHits.Select(n => new SearchHitDto(
                n.Kind == NoteKind.Drawing ? SearchHitTypes.Drawing : SearchHitTypes.Note,
                n.Id, n.Title, n.TaskItem?.Title ?? n.Project?.Name)))
            .ToList();

        return Ok(results);
    }
}
