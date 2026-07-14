using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using HorizonNET.Shared.Transfer.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace HorizonNET.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NotesController(INoteRepository repo) : ControllerBase
{
    private static NoteResponseDto ToDto(Note n) =>
        new(n.Id, n.Title, n.Content, n.CreatedAt, n.UpdatedAt,
            n.TaskItemId, n.TaskItem?.Title, n.ProjectId, n.Project?.Name);

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var notes = await repo.GetAllAsync();
        return Ok(notes.Select(ToDto));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var note = await repo.GetByIdAsync(id);
        if (note is null) return NotFound();
        return Ok(ToDto(note));
    }

    [HttpGet("task/{taskId:int}")]
    public async Task<IActionResult> GetByTask(int taskId)
    {
        var notes = await repo.GetByTaskIdAsync(taskId);
        return Ok(notes.Select(ToDto));
    }

    [HttpGet("project/{projectId:int}")]
    public async Task<IActionResult> GetByProject(int projectId)
    {
        var notes = await repo.GetByProjectIdAsync(projectId);
        return Ok(notes.Select(ToDto));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] NoteCreateDto dto)
    {
        var note = new Note
        {
            Title = dto.Title,
            Content = dto.Content ?? string.Empty,
            TaskItemId = dto.TaskItemId,
            ProjectId = dto.ProjectId
        };
        var created = await repo.CreateAsync(note);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, ToDto(created));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] NoteUpdateDto dto)
    {
        var updated = await repo.UpdateAsync(id, new Note
        {
            Title = dto.Title,
            Content = dto.Content ?? string.Empty,
            TaskItemId = dto.TaskItemId,
            ProjectId = dto.ProjectId
        });
        if (updated is null) return NotFound();
        return Ok(ToDto(updated));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await repo.DeleteAsync(id);
        if (!deleted) return NotFound();
        return NoContent();
    }

    [HttpPost("{id:int}/restore")]
    public async Task<IActionResult> Restore(int id)
    {
        var restored = await repo.RestoreAsync(id);
        if (!restored) return NotFound();
        return NoContent();
    }
}
