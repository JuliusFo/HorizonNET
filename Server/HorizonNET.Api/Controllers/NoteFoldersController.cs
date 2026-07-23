using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using HorizonNET.Shared.Transfer.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace HorizonNET.Api.Controllers;

[ApiController]
[Route("api/note-folders")]
public class NoteFoldersController(INoteFolderRepository repo) : ControllerBase
{
    private static NoteFolderResponseDto ToDto(NoteFolder f) =>
        new(f.Id, f.Name, f.ParentFolderId, f.CreatedAt);

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var folders = await repo.GetAllAsync();
        return Ok(folders.Select(ToDto));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var folder = await repo.GetByIdAsync(id);
        if (folder is null) return NotFound();
        return Ok(ToDto(folder));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] NoteFolderCreateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("Name ist erforderlich.");

        var created = await repo.CreateAsync(new NoteFolder
        {
            Name = dto.Name.Trim(),
            ParentFolderId = dto.ParentFolderId
        });
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, ToDto(created));
    }

    [HttpPut("{id:int}/name")]
    public async Task<IActionResult> Rename(int id, [FromBody] NoteFolderRenameDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("Name ist erforderlich.");

        var updated = await repo.RenameAsync(id, dto.Name.Trim());
        if (updated is null) return NotFound();
        return Ok(ToDto(updated));
    }

    // Getrennt vom Umbenennen, weil hier die Zyklusprüfung hängt: Ein Ordner darf nicht
    // unter sich selbst oder einen eigenen Nachfahren wandern.
    [HttpPut("{id:int}/parent")]
    public async Task<IActionResult> Move(int id, [FromBody] NoteFolderMoveDto dto)
    {
        if (await repo.GetByIdAsync(id) is null) return NotFound();

        var moved = await repo.MoveAsync(id, dto.ParentFolderId);
        if (moved is null) return BadRequest("Ein Ordner kann nicht unter sich selbst liegen.");
        return Ok(ToDto(moved));
    }

    // Soft-Delete inkl. Unterordner. Die Notizen darin bleiben erhalten und behalten ihre
    // Zuordnung – nach dem Wiederherstellen liegen sie wieder im Ordner.
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id) =>
        await repo.DeleteAsync(id) ? NoContent() : NotFound();

    [HttpPost("{id:int}/restore")]
    public async Task<IActionResult> Restore(int id) =>
        await repo.RestoreAsync(id) ? NoContent() : NotFound();
}
