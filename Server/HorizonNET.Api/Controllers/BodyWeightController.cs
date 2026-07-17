using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using HorizonNET.Shared.Transfer.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace HorizonNET.Api.Controllers;

[ApiController]
[Route("api/bodyweight")]
public class BodyWeightController(IBodyWeightRepository repo) : ControllerBase
{
    private static BodyWeightResponseDto ToDto(BodyWeightEntry b) =>
        new(b.Id, b.MeasuredOn, b.WeightKg);

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] DateOnly? from, [FromQuery] DateOnly? to) =>
        Ok((await repo.GetAsync(from, to)).Select(ToDto));

    // Anlegen UND Korrigieren: höchstens ein Wert pro Tag, ein zweiter überschreibt.
    [HttpPut]
    public async Task<IActionResult> Set([FromBody] BodyWeightSetDto dto)
    {
        if (dto.WeightKg is <= 0 or > 500)
            return BadRequest("Gewicht muss zwischen 0 und 500 kg liegen.");

        var entry = await repo.SetAsync(dto.MeasuredOn, dto.WeightKg);
        return Ok(ToDto(entry));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id) =>
        await repo.DeleteAsync(id) ? NoContent() : NotFound();

    [HttpPost("{id:int}/restore")]
    public async Task<IActionResult> Restore(int id) =>
        await repo.RestoreAsync(id) ? NoContent() : NotFound();
}
