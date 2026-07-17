using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using HorizonNET.Shared.Transfer.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace HorizonNET.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ExercisesController(IExerciseRepository repo) : ControllerBase
{
    private static ExerciseResponseDto ToDto(Exercise x) =>
        new(x.Id, x.Name, x.Kind, x.Notes, x.IsActive, x.SortOrder, x.CreatedAt);

    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok((await repo.GetAllAsync()).Select(ToDto));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var exercise = await repo.GetByIdAsync(id);
        return exercise is null ? NotFound() : Ok(ToDto(exercise));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ExerciseCreateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("Name ist erforderlich.");

        var created = await repo.CreateAsync(new Exercise
        {
            Name = dto.Name.Trim(),
            Kind = dto.Kind,
            Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim()
        });

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, ToDto(created));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ExerciseUpdateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("Name ist erforderlich.");

        var updated = await repo.UpdateAsync(id, new Exercise
        {
            Name = dto.Name.Trim(),
            Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim(),
            IsActive = dto.IsActive
        });

        return updated is null ? NotFound() : Ok(ToDto(updated));
    }

    [HttpPut("reorder")]
    public async Task<IActionResult> Reorder([FromBody] List<int> orderedIds)
    {
        await repo.ReorderAsync(orderedIds);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id) =>
        await repo.DeleteAsync(id) ? NoContent() : NotFound();

    [HttpPost("{id:int}/restore")]
    public async Task<IActionResult> Restore(int id) =>
        await repo.RestoreAsync(id) ? NoContent() : NotFound();
}
