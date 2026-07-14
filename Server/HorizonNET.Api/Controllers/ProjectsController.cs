using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using HorizonNET.Shared.Transfer.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace HorizonNET.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProjectsController(IProjectRepository repo) : ControllerBase
{
    private static ProjectResponseDto ToDto(Project p) =>
        new(p.Id, p.Name, p.Description, p.Status.ToString(), p.Priority.ToString(), p.CreatedAt, p.Tasks.Count, p.Tasks.Count(t => t.IsCompleted), p.Color, p.WorkspaceId);

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var projects = await repo.GetAllAsync();
        return Ok(projects.Select(ToDto));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var project = await repo.GetByIdAsync(id);
        if (project is null) return NotFound();
        return Ok(ToDto(project));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ProjectCreateDto dto)
    {
        var project = new Project
        {
            Name = dto.Name,
            Description = dto.Description,
            Status = dto.Status,
            Priority = dto.Priority,
            Color = dto.Color,
            WorkspaceId = dto.WorkspaceId
        };
        var created = await repo.CreateAsync(project);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, ToDto(created));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ProjectUpdateDto dto)
    {
        var updated = await repo.UpdateAsync(id, new Project
        {
            Name = dto.Name,
            Description = dto.Description,
            Status = dto.Status,
            Priority = dto.Priority,
            Color = dto.Color,
            WorkspaceId = dto.WorkspaceId
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

    // Macht ein Soft-Delete rückgängig (Projekt + im selben Vorgang gelöschte Tasks).
    [HttpPost("{id:int}/restore")]
    public async Task<IActionResult> Restore(int id)
    {
        var restored = await repo.RestoreAsync(id);
        if (!restored) return NotFound();
        return NoContent();
    }
}
