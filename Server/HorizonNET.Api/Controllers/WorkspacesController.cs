using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using HorizonNET.Shared.Transfer.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace HorizonNET.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WorkspacesController(IWorkspaceRepository repo) : ControllerBase
{
    private static WorkspaceResponseDto ToDto(Workspace w) =>
        new(w.Id, w.Name, w.Description, w.CreatedAt, w.Projects.Count, w.Color);

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var workspaces = await repo.GetAllAsync();
        return Ok(workspaces.Select(ToDto));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var workspace = await repo.GetByIdAsync(id);
        if (workspace is null) return NotFound();
        return Ok(ToDto(workspace));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] WorkspaceCreateDto dto)
    {
        var workspace = new Workspace
        {
            Name = dto.Name,
            Description = dto.Description,
            Color = dto.Color
        };
        var created = await repo.CreateAsync(workspace);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, ToDto(created));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] WorkspaceUpdateDto dto)
    {
        var updated = await repo.UpdateAsync(id, new Workspace
        {
            Name = dto.Name,
            Description = dto.Description,
            Color = dto.Color
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
}
