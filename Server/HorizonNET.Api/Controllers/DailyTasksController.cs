using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using HorizonNET.Shared.Transfer.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace HorizonNET.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DailyTasksController(IDailyTaskRepository repo) : ControllerBase
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    private static DailyTaskResponseDto ToDto(DailyTask t, DateOnly today)
    {
        var dates = t.Completions.Select(c => c.Date).ToHashSet();
        return new DailyTaskResponseDto(
            t.Id, t.Title, t.SortOrder, t.IsActive, t.ProjectId, t.Project?.Name,
            t.WeekdayMask,
            CompletedToday: dates.Contains(today),
            CurrentStreak: ComputeStreak(dates, today, t.WeekdayMask));
    }

    // Ist der Tag im Wochentags-Muster geplant? Bit-Index = (int)DayOfWeek (So=0 … Sa=6).
    private static bool IsScheduled(byte mask, DateOnly d) => (mask & (1 << (int)d.DayOfWeek)) != 0;

    // Nächster geplanter Tag rückwärts (überspringt ungeplante Wochentage).
    private static DateOnly PrevScheduled(byte mask, DateOnly d)
    {
        do { d = d.AddDays(-1); } while (!IsScheduled(mask, d));
        return d;
    }

    // Serie: von heute rückwärts zählen, aber NUR geplante Tage betrachten – ein geplanter
    // Tag ohne Häkchen bricht die Serie, ungeplante Tage werden übersprungen (z. B. Wochenende
    // bei "Mo–Fr"). Ist heute ein geplanter, noch offener Tag, wird ab dem vorherigen geplanten
    // Tag gezählt (die Serie "lebt" bis zum Tagesende weiter).
    private static int ComputeStreak(HashSet<DateOnly> dates, DateOnly today, byte mask)
    {
        if (mask == 0) return 0;

        var day = today;
        while (!IsScheduled(mask, day)) day = day.AddDays(-1); // jüngster geplanter Tag <= heute

        if (day == today && !dates.Contains(today))
            day = PrevScheduled(mask, day);

        var streak = 0;
        while (dates.Contains(day))
        {
            streak++;
            day = PrevScheduled(mask, day);
        }
        return streak;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var today = Today;
        var tasks = await repo.GetAllAsync();
        return Ok(tasks.Select(t => ToDto(t, today)));
    }

    [HttpGet("today")]
    public async Task<IActionResult> GetToday()
    {
        var today = Today;
        var tasks = await repo.GetActiveAsync();
        return Ok(tasks.Where(t => IsScheduled(t.WeekdayMask, today)).Select(t => ToDto(t, today)));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var task = await repo.GetByIdAsync(id);
        if (task is null) return NotFound();
        return Ok(ToDto(task, Today));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] DailyTaskCreateDto dto)
    {
        var task = new DailyTask { Title = dto.Title, ProjectId = dto.ProjectId, WeekdayMask = dto.WeekdayMask };
        var created = await repo.CreateAsync(task);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, ToDto(created, Today));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] DailyTaskUpdateDto dto)
    {
        var updated = await repo.UpdateAsync(id, new DailyTask
        {
            Title = dto.Title,
            IsActive = dto.IsActive,
            ProjectId = dto.ProjectId,
            WeekdayMask = dto.WeekdayMask
        });
        if (updated is null) return NotFound();
        return Ok(ToDto(updated, Today));
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

    [HttpPut("reorder")]
    public async Task<IActionResult> Reorder([FromBody] List<int> orderedIds)
    {
        await repo.ReorderAsync(orderedIds);
        return NoContent();
    }

    // Häkchen für einen Tag setzen (Standard: heute).
    [HttpPost("{id:int}/complete")]
    public async Task<IActionResult> Complete(int id, [FromQuery] DateOnly? date)
    {
        var ok = await repo.SetCompletionAsync(id, date ?? Today, completed: true);
        return ok ? NoContent() : NotFound();
    }

    // Häkchen für einen Tag entfernen (Standard: heute).
    [HttpDelete("{id:int}/complete")]
    public async Task<IActionResult> Uncomplete(int id, [FromQuery] DateOnly? date)
    {
        var ok = await repo.SetCompletionAsync(id, date ?? Today, completed: false);
        return ok ? NoContent() : NotFound();
    }
}
