using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using HorizonNET.Shared.Transfer.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace HorizonNET.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TaskTemplatesController(ITaskTemplateRepository repo) : ControllerBase
{
    private static TaskTemplateResponseDto ToDto(TaskTemplate t) => new(
        t.Id, t.Title, t.Description, t.Priority.ToString(),
        t.ProjectId, t.Project?.Name, t.SortOrder);

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var templates = await repo.GetAllAsync();
        return Ok(templates.Select(ToDto));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var template = await repo.GetByIdAsync(id);
        if (template is null) return NotFound();
        return Ok(ToDto(template));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] TaskTemplateCreateDto dto)
    {
        var created = await repo.CreateAsync(new TaskTemplate
        {
            Title = dto.Title,
            Description = dto.Description,
            Priority = dto.Priority,
            ProjectId = dto.ProjectId
        });
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, ToDto(created));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] TaskTemplateUpdateDto dto)
    {
        var updated = await repo.UpdateAsync(id, new TaskTemplate
        {
            Title = dto.Title,
            Description = dto.Description,
            Priority = dto.Priority,
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
