using HorizonNET.Api.Services;
using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using HorizonNET.Shared.Transfer;
using HorizonNET.Shared.Transfer.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace HorizonNET.Api.Controllers;

// CRUD für Terminserien (Schablonen). Nach Anlegen/Ändern läuft der Generator sofort,
// damit die Termine im Kalender stehen, ohne auf den nächtlichen Lauf zu warten.
// Google-Sync für geänderte/entfernte Termine liegt hier (über den Generator), nicht im
// Repository – wie beim TasksController.
[ApiController]
[Route("api/[controller]")]
public class TaskSeriesController(ITaskSeriesRepository repo, TaskSeriesGenerator generator) : ControllerBase
{
    private static TaskSeriesResponseDto ToDto(TaskSeries s) =>
        new(s.Id, s.Title, s.Description, s.ProjectId, s.Project?.Name,
            s.IsActive, s.Kind, s.Priority.ToString(), s.ReminderMinutes,
            s.Slots
                .OrderBy(x => x.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)x.DayOfWeek) // Mo … So
                .ThenBy(x => x.StartTime)
                .Select(x => new TaskSeriesSlotDto(x.DayOfWeek, x.StartTime, x.EndTime, x.Id))
                .ToList(),
            s.CreatedAt, s.UpdatedAt);

    private static List<TaskSeriesSlot> ToSlots(IEnumerable<TaskSeriesSlotDto> slots) =>
        slots.Select(x => new TaskSeriesSlot
        {
            Id = x.Id ?? 0,
            DayOfWeek = x.DayOfWeek,
            StartTime = x.StartTime,
            EndTime = x.EndTime
        }).ToList();

    // Was an einer Serie schiefgehen kann, bevor sie in die Datenbank darf. null = ok.
    private static string? Validate(string title, IEnumerable<TaskSeriesSlotDto> slots, int? reminderMinutes)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "Titel ist erforderlich.";
        if (!TaskReminder.IsValid(reminderMinutes))
            return "Erinnerung liegt außerhalb des erlaubten Bereichs.";
        if (slots.Any(s => s.EndTime <= s.StartTime))
            return "Ende muss nach dem Beginn liegen.";
        if (slots.Any(s => !Enum.IsDefined(s.DayOfWeek)))
            return "Ungültiger Wochentag.";
        return null;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var series = await repo.GetAllAsync();
        return Ok(series.Select(ToDto));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var series = await repo.GetByIdAsync(id);
        if (series is null) return NotFound();
        return Ok(ToDto(series));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] TaskSeriesCreateDto dto)
    {
        if (Validate(dto.Title, dto.Slots, dto.ReminderMinutes) is string error)
            return BadRequest(error);

        var created = await repo.CreateAsync(new TaskSeries
        {
            Title = dto.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description,
            ProjectId = dto.ProjectId,
            Kind = dto.Kind,
            Priority = dto.Priority,
            ReminderMinutes = dto.ReminderMinutes,
            Slots = ToSlots(dto.Slots)
        });

        await generator.RunAsync(); // Termine sofort materialisieren (best-effort Google-Sync inklusive)
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, ToDto(created));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] TaskSeriesUpdateDto dto)
    {
        if (Validate(dto.Title, dto.Slots, dto.ReminderMinutes) is string error)
            return BadRequest(error);

        var change = await repo.UpdateAsync(id, new TaskSeries
        {
            Title = dto.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description,
            ProjectId = dto.ProjectId,
            IsActive = dto.IsActive,
            Kind = dto.Kind,
            Priority = dto.Priority,
            ReminderMinutes = dto.ReminderMinutes,
            Slots = ToSlots(dto.Slots)
        });
        if (change is null) return NotFound();

        await generator.SyncAsync(change);  // geänderte/entfernte Termine in Google nachziehen
        await generator.RunAsync();         // neue Slots sofort materialisieren
        return Ok(ToDto(change.Series));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var change = await repo.DeleteAsync(id);
        if (change is null) return NotFound();

        await generator.SyncAsync(change); // Google-Events der mitgelöschten Termine entfernen
        return NoContent();
    }

    [HttpPost("{id:int}/restore")]
    public async Task<IActionResult> Restore(int id)
    {
        var change = await repo.RestoreAsync(id);
        if (change is null) return NotFound();

        await generator.SyncAsync(change); // zurückgeholte Termine wieder spiegeln
        return NoContent();
    }
}
