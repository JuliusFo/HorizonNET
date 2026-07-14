using HorizonNET.Api.Services;
using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using HorizonNET.Shared.Transfer.DTOs;
using HorizonNET.Shared.Transfer.Enums;
using Microsoft.AspNetCore.Mvc;

namespace HorizonNET.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TasksController(
    ITaskRepository repo,
    ITimeEntryRepository timeEntries,
    GoogleCalendarService google) : ControllerBase
{
    private static TaskResponseDto ToDto(TaskItem t) =>
        new(t.Id, t.Title, t.Description, t.DueDate, t.StartTime, t.EndTime,
            t.Status, t.Priority.ToString(), t.ProjectId, t.Project?.Name,
            t.SortOrder,
            t.ParentTaskId,
            t.SubTasks.Count > 0 ? t.SubTasks.OrderBy(s => s.SortOrder).Select(s => ToDto(s)).ToList() : null,
            t.CreatedAt, t.UpdatedAt, t.GoogleEventId != null,
            // Nur abgeschlossene Intervalle summieren; das laufende meldet RunningSince,
            // damit der Client die Uhr selbst weiterzählen kann.
            TrackedSeconds: (int)t.TimeEntries
                .Where(e => e.EndedAt != null)
                .Sum(e => (e.EndedAt!.Value - e.StartedAt).TotalSeconds),
            RunningSince: t.TimeEntries.FirstOrDefault(e => e.EndedAt == null)?.StartedAt);

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var tasks = await repo.GetAllAsync();
        return Ok(tasks.Select(ToDto));
    }

    [HttpGet("project/{projectId:int}")]
    public async Task<IActionResult> GetByProject(int projectId)
    {
        var tasks = await repo.GetByProjectIdAsync(projectId);
        return Ok(tasks.Select(ToDto));
    }

    [HttpGet("inbox")]
    public async Task<IActionResult> GetInbox()
    {
        var tasks = await repo.GetInboxAsync();
        return Ok(tasks.Select(ToDto));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var task = await repo.GetByIdAsync(id);
        if (task is null) return NotFound();
        return Ok(ToDto(task));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] TaskCreateDto dto)
    {
        var task = new TaskItem
        {
            Title = dto.Title,
            Description = dto.Description,
            DueDate = dto.DueDate,
            // Ohne Fälligkeitsdatum keine Uhrzeit (Invariante, unabhängig vom Aufrufer).
            StartTime = dto.DueDate is null ? null : dto.StartTime,
            EndTime = dto.DueDate is null ? null : dto.EndTime,
            Priority = dto.Priority,
            ProjectId = dto.ProjectId,
            ParentTaskId = dto.ParentTaskId,
            Status = dto.Status
        };
        var created = await repo.CreateAsync(task);
        await google.SyncTaskAsync(created); // geplanten Task in Google spiegeln (best-effort)
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, ToDto(created));
    }

    [HttpPut("reorder")]
    public async Task<IActionResult> Reorder([FromBody] TaskReorderDto dto)
    {
        await repo.ReorderAsync(dto.Status, dto.OrderedTaskIds);
        return NoContent();
    }

    [HttpPut("reorder-subtasks")]
    public async Task<IActionResult> ReorderSubTasks([FromBody] List<int> orderedTaskIds)
    {
        await repo.ReorderSubTasksAsync(orderedTaskIds);
        return NoContent();
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] TaskUpdateDto dto)
    {
        var updated = await repo.UpdateAsync(id, new TaskItem
        {
            Title = dto.Title,
            Description = dto.Description,
            DueDate = dto.DueDate,
            // Ohne Fälligkeitsdatum keine Uhrzeit (Invariante, unabhängig vom Aufrufer).
            StartTime = dto.DueDate is null ? null : dto.StartTime,
            EndTime = dto.DueDate is null ? null : dto.EndTime,
            Status = dto.Status,
            Priority = dto.Priority,
            ProjectId = dto.ProjectId
        });
        if (updated is null) return NotFound();
        await google.SyncTaskAsync(updated); // Änderung nach Google spiegeln (best-effort)
        return Ok(ToDto(updated));
    }

    // ── Zeiterfassung ────────────────────────────────────────────────────────────
    // Start/Stop laufen über den Status: "In Arbeit" startet den Timer, jeder Wechsel
    // weg davon stoppt ihn (siehe TaskRepository). Damit bleibt die Kopplung an genau
    // einer Stelle, egal ob der Nutzer den Status ändert oder den Timer-Knopf drückt.

    [HttpPost("{id:int}/timer/start")]
    public async Task<IActionResult> StartTimer(int id)
    {
        var task = await repo.GetByIdAsync(id);
        if (task is null) return NotFound();

        var updated = await SetStatusAsync(task, WorkStatus.InProgress);
        return Ok(ToDto(updated));
    }

    // Stoppen setzt den Task auf "Pausiert" – der Status spiegelt die Uhr wider.
    // Ein bereits abgeschlossener Task (Fertig/Abgebrochen) behält seinen Status.
    [HttpPost("{id:int}/timer/stop")]
    public async Task<IActionResult> StopTimer(int id)
    {
        var task = await repo.GetByIdAsync(id);
        if (task is null) return NotFound();

        if (task.Status != WorkStatus.InProgress)
        {
            // Kein laufender Timer über den Status – trotzdem sicherheitshalber stoppen
            // (z. B. wenn der Status außerhalb der Kopplung verändert wurde).
            await timeEntries.StopAsync(id);
            var reloaded = await repo.GetByIdAsync(id);
            return Ok(ToDto(reloaded!));
        }

        var updated = await SetStatusAsync(task, WorkStatus.Paused);
        return Ok(ToDto(updated));
    }

    // Der aktuell laufende Timer (systemweit höchstens einer) – für die Navigation.
    [HttpGet("timer/running")]
    public async Task<IActionResult> GetRunningTimer()
    {
        var running = await timeEntries.GetRunningAsync();
        if (running is null) return Ok(null);

        return Ok(new RunningTimerDto(running.TaskItemId, running.TaskItem.Title, running.StartedAt));
    }

    // Alle Intervalle eines Tasks (Detailseite).
    [HttpGet("{id:int}/timeentries")]
    public async Task<IActionResult> GetTimeEntries(int id)
    {
        var entries = await timeEntries.GetByTaskAsync(id);
        return Ok(entries.Select(e => new TimeEntryResponseDto(
            e.Id, e.TaskItemId, e.StartedAt, e.EndedAt,
            (int)(e.EndedAt is null ? 0 : (e.EndedAt.Value - e.StartedAt).TotalSeconds))));
    }

    // Statuswechsel über das Repository – dort hängt die Timer-Kopplung dran.
    private async Task<TaskItem> SetStatusAsync(TaskItem task, WorkStatus status)
    {
        var updated = await repo.UpdateAsync(task.Id, new TaskItem
        {
            Title = task.Title,
            Description = task.Description,
            DueDate = task.DueDate,
            StartTime = task.StartTime,
            EndTime = task.EndTime,
            Status = status,
            Priority = task.Priority,
            ProjectId = task.ProjectId
        });
        return updated!;
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        // Event-ID vor dem Löschen merken, um den Google-Eintrag danach zu entfernen.
        var task = await repo.GetByIdAsync(id);
        if (task is null) return NotFound();
        var googleEventId = task.GoogleEventId;

        var deleted = await repo.DeleteAsync(id);
        if (!deleted) return NotFound();

        if (!string.IsNullOrEmpty(googleEventId))
            await google.DeleteTaskEventAsync(googleEventId); // best-effort

        return NoContent();
    }

    [HttpPost("{id:int}/restore")]
    public async Task<IActionResult> Restore(int id)
    {
        var restored = await repo.RestoreAsync(id);
        if (!restored) return NotFound();

        // Wiederhergestellten Task erneut nach Google spiegeln (best-effort);
        // SyncTaskAsync legt bei ungültiger Event-ID (404/410) einen neuen Termin an.
        var task = await repo.GetByIdAsync(id);
        if (task is not null)
            await google.SyncTaskAsync(task);

        return NoContent();
    }
}
