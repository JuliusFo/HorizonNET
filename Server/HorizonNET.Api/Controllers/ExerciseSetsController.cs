using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using HorizonNET.Shared.Transfer.DTOs;
using HorizonNET.Shared.Transfer.Enums;
using Microsoft.AspNetCore.Mvc;

namespace HorizonNET.Api.Controllers;

[ApiController]
[Route("api/exercise-sets")]
public class ExerciseSetsController(IExerciseSetRepository repo, IExerciseRepository exercises) : ControllerBase
{
    private static ExerciseSetResponseDto ToDto(ExerciseSet s) =>
        new(s.Id, s.ExerciseId, s.Exercise?.Name ?? string.Empty, s.Exercise?.Kind ?? ExerciseKind.Strength,
            s.PerformedAt, s.SetNumber, s.Reps, s.WeightKg, s.DistanceMeters, s.DurationSeconds,
            s.Rpe, s.Notes);

    // Prüft die Werte gegen die Art der Übung. Serverseitig, damit die Regel für jeden
    // Aufrufer gilt: Ein Laufeintrag ohne Strecke oder ein Kraftsatz ohne Wiederholungen
    // wäre in der Auswertung ein stiller Ausreißer statt eines Fehlers.
    private static string? Validate(ExerciseKind kind, int? reps, double? weightKg,
                                    int? distanceMeters, int? durationSeconds, int? rpe)
    {
        if (rpe is < 1 or > 10) return "Anstrengung muss zwischen 1 und 10 liegen.";
        if (reps is <= 0) return "Wiederholungen müssen größer als 0 sein.";
        if (weightKg is < 0) return "Gewicht darf nicht negativ sein.";
        if (distanceMeters is <= 0) return "Strecke muss größer als 0 sein.";
        if (durationSeconds is <= 0) return "Dauer muss größer als 0 sein.";

        return kind switch
        {
            ExerciseKind.Strength when reps is null || weightKg is null
                => "Kraftübungen brauchen Wiederholungen und Gewicht.",
            ExerciseKind.Bodyweight when reps is null
                => "Körpergewichts-Übungen brauchen Wiederholungen.",
            ExerciseKind.Endurance when distanceMeters is null && durationSeconds is null
                => "Ausdauer-Übungen brauchen Strecke oder Dauer.",
            _ => null
        };
    }

    // Zeitraum: 'to' ist exklusiv (Repository filtert < to), damit ein ganzer Tag
    // schlicht als from=Tag, to=Tag+1 anfragbar ist.
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] DateTime? from, [FromQuery] DateTime? to,
                                         [FromQuery] int? exerciseId) =>
        Ok((await repo.GetAsync(from, to, exerciseId)).Select(ToDto));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var set = await repo.GetByIdAsync(id);
        return set is null ? NotFound() : Ok(ToDto(set));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ExerciseSetCreateDto dto)
    {
        var exercise = await exercises.GetByIdAsync(dto.ExerciseId);
        if (exercise is null) return BadRequest("Übung nicht gefunden.");

        var error = Validate(exercise.Kind, dto.Reps, dto.WeightKg, dto.DistanceMeters,
                             dto.DurationSeconds, dto.Rpe);
        if (error is not null) return BadRequest(error);

        var created = await repo.CreateAsync(new ExerciseSet
        {
            ExerciseId = dto.ExerciseId,
            PerformedAt = dto.PerformedAt,
            Reps = dto.Reps,
            WeightKg = dto.WeightKg,
            DistanceMeters = dto.DistanceMeters,
            DurationSeconds = dto.DurationSeconds,
            Rpe = dto.Rpe,
            Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim()
        });

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, ToDto(created));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ExerciseSetUpdateDto dto)
    {
        var existing = await repo.GetByIdAsync(id);
        if (existing is null) return NotFound();

        var kind = existing.Exercise?.Kind ?? ExerciseKind.Strength;
        var error = Validate(kind, dto.Reps, dto.WeightKg, dto.DistanceMeters,
                             dto.DurationSeconds, dto.Rpe);
        if (error is not null) return BadRequest(error);

        var updated = await repo.UpdateAsync(id, new ExerciseSet
        {
            PerformedAt = dto.PerformedAt,
            Reps = dto.Reps,
            WeightKg = dto.WeightKg,
            DistanceMeters = dto.DistanceMeters,
            DurationSeconds = dto.DurationSeconds,
            Rpe = dto.Rpe,
            Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim()
        });

        return updated is null ? NotFound() : Ok(ToDto(updated));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id) =>
        await repo.DeleteAsync(id) ? NoContent() : NotFound();

    [HttpPost("{id:int}/restore")]
    public async Task<IActionResult> Restore(int id) =>
        await repo.RestoreAsync(id) ? NoContent() : NotFound();
}
