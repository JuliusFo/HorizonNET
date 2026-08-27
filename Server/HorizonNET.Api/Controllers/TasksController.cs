using HorizonNET.Api.Services;
using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using HorizonNET.Shared.Transfer;
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
            t.Link,
            t.WaitingFor,
            t.SortOrder,
            t.ListSortOrder,
            t.ParentTaskId,
            t.SubTasks.Count > 0 ? t.SubTasks.OrderBy(s => s.SortOrder).Select(s => ToDto(s)).ToList() : null,
            t.CreatedAt, t.UpdatedAt, t.GoogleEventId != null,
            // Nur abgeschlossene Intervalle summieren; das laufende meldet RunningSince,
            // damit der Client die Uhr selbst weiterzählen kann.
            TrackedSeconds: (int)t.TimeEntries
                .Where(e => e.EndedAt != null)
                .Sum(e => (e.EndedAt!.Value - e.StartedAt).TotalSeconds),
            RunningSince: t.TimeEntries.FirstOrDefault(e => e.EndedAt == null)?.StartedAt,
            ReminderMinutes: t.ReminderMinutes);

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

    // Schlanke Liste für Auswahlfelder: Id, Titel, Projekt, Sub-Tasks. Wer einen Task
    // anzeigen oder bearbeiten will, nimmt weiterhin GET /api/tasks – dieser Endpunkt
    // existiert allein, damit eine Klappliste nicht die gesamte Zeiterfassung mitzieht.
    [HttpGet("options")]
    public async Task<IActionResult> GetOptions() => Ok(await repo.GetOptionsAsync());

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
        if (!TaskReminder.IsValid(dto.ReminderMinutes))
            return BadRequest("Erinnerung liegt außerhalb des erlaubten Bereichs.");

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
            Status = dto.Status,
            ReminderMinutes = dto.ReminderMinutes
        };
        var created = await repo.CreateAsync(task);
        await google.SyncTaskAsync(created); // geplanten Task in Google spiegeln (best-effort)
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, ToDto(created));
    }

    [HttpPut("reorder")]
    public async Task<IActionResult> Reorder([FromBody] TaskReorderDto dto)
    {
        // Ein Statuswechsel kann das Fälligkeitsdatum setzen ("Geplant Heute"), und das
        // gehört nach Google. Welche Tasks das betrifft, weiß das Repository – es hat den
        // Wechsel gerade vollzogen. Wer nur seine Position in der Spalte ändert, ist nicht
        // dabei und kostet damit auch keinen Netzaufruf.
        var rescheduled = await repo.ReorderAsync(dto.Status, dto.OrderedTaskIds, dto.CompleteSubTasks);

        foreach (var task in rescheduled)
            await google.SyncTaskAsync(task); // best-effort

        return NoContent();
    }

    [HttpPut("reorder-subtasks")]
    public async Task<IActionResult> ReorderSubTasks([FromBody] List<int> orderedTaskIds)
    {
        await repo.ReorderSubTasksAsync(orderedTaskIds);
        return NoContent();
    }

    // Reihenfolge der Haupt-Tasks in der Projektliste. Kein Google-Sync nötig: weder
    // Status noch Fälligkeitsdatum ändern sich (anders als bei "reorder").
    [HttpPut("reorder-list")]
    public async Task<IActionResult> ReorderTaskList([FromBody] List<int> orderedTaskIds)
    {
        await repo.ReorderTaskListAsync(orderedTaskIds);
        return NoContent();
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] TaskUpdateDto dto)
    {
        // Link-Regel gilt in der API, nicht nur im Formular (Invariante, unabhängig
        // vom Aufrufer – wie die Uhrzeit-Regel darunter).
        var link = string.IsNullOrWhiteSpace(dto.Link) ? null : dto.Link.Trim();
        if (link is not null && !TaskLink.IsValid(link))
            return BadRequest("Link muss mit http:// oder https:// beginnen.");

        if (!TaskReminder.IsValid(dto.ReminderMinutes))
            return BadRequest("Erinnerung liegt außerhalb des erlaubten Bereichs.");

        var updated = await repo.UpdateAsync(id, new TaskItem
        {
            Title = dto.Title,
            Description = dto.Description,
            // Uhrzeit-Invariante (ohne Fälligkeit keine Uhrzeit) setzt das Repository durch –
            // dort gilt sie für jeden Weg, der Termine schreibt.
            DueDate = dto.DueDate,
            StartTime = dto.StartTime,
            EndTime = dto.EndTime,
            Status = dto.Status,
            Priority = dto.Priority,
            ProjectId = dto.ProjectId,
            Link = link,
            WaitingFor = string.IsNullOrWhiteSpace(dto.WaitingFor) ? null : dto.WaitingFor.Trim(),
            ReminderMinutes = dto.ReminderMinutes
        }, dto.CompleteSubTasks);
        if (updated is null) return NotFound();
        await google.SyncTaskAsync(updated); // Änderung nach Google spiegeln (best-effort)
        return Ok(ToDto(updated));
    }

    // ── Teil-Updates ─────────────────────────────────────────────────────────────
    // Für Aufrufer mit genau einem Anliegen (abhaken, verschieben, umhängen). Sie müssen
    // den restlichen Task weder kennen noch zurückschicken – dadurch können sie weder
    // neue Felder übersehen noch mit einem veralteten Stand fremde Änderungen zurückrollen.

    [HttpPut("{id:int}/status")]
    public async Task<IActionResult> SetStatus(int id, [FromBody] TaskStatusDto dto)
    {
        var updated = await repo.SetStatusAsync(id, dto.Status, dto.CompleteSubTasks);
        if (updated is null) return NotFound();

        // Ein Statuswechsel kann das Fälligkeitsdatum setzen ("Geplant Heute") – deshalb
        // wie beim Vollersatz nach Google spiegeln.
        await google.SyncTaskAsync(updated); // best-effort
        return Ok(ToDto(updated));
    }

    [HttpPut("{id:int}/schedule")]
    public async Task<IActionResult> SetSchedule(int id, [FromBody] TaskScheduleDto dto)
    {
        var updated = await repo.SetScheduleAsync(id, dto.DueDate, dto.StartTime, dto.EndTime);
        if (updated is null) return NotFound();
        await google.SyncTaskAsync(updated); // best-effort
        return Ok(ToDto(updated));
    }

    [HttpPut("{id:int}/project")]
    public async Task<IActionResult> SetProject(int id, [FromBody] TaskProjectDto dto)
    {
        var updated = await repo.SetProjectAsync(id, dto.ProjectId);
        if (updated is null) return NotFound();
        await google.SyncTaskAsync(updated); // best-effort
        return Ok(ToDto(updated));
    }

    // ── Zeiterfassung ────────────────────────────────────────────────────────────
    // Start/Stop laufen über den Status: "In Arbeit" startet den Timer, jeder Wechsel
    // weg davon stoppt ihn (siehe TaskRepository). Damit bleibt die Kopplung an genau
    // einer Stelle, egal ob der Nutzer den Status ändert oder den Timer-Knopf drückt.
    //
    // Bewusst über das Teil-Update SetStatusAsync und NICHT über UpdateAsync: Wer den
    // Timer drückt, will nur den Status ändern und schickt die übrigen Felder gar nicht
    // mit. Der Vollersatz würde alles, was der Aufrufer nicht kennt, mit null
    // überschreiben – genau das hat hier einmal Link und "Warten auf" gelöscht.

    [HttpPost("{id:int}/timer/start")]
    public async Task<IActionResult> StartTimer(int id)
    {
        var updated = await repo.SetStatusAsync(id, WorkStatus.InProgress);
        if (updated is null) return NotFound();

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

        var updated = await repo.SetStatusAsync(id, WorkStatus.Paused);
        if (updated is null) return NotFound();

        return Ok(ToDto(updated));
    }

    // Der aktuell laufende Timer (systemweit höchstens einer) – für die Kopfzeile.
    [HttpGet("timer/running")]
    public async Task<IActionResult> GetRunningTimer()
    {
        var running = await timeEntries.GetRunningAsync();
        if (running is null) return Ok(null);

        // Nur die abgeschlossenen Intervalle summieren – wie in ToDto. Das laufende
        // zählt die Anzeige aus StartedAt selbst weiter.
        var entries = await timeEntries.GetByTaskAsync(running.TaskItemId);
        var tracked = (int)entries
            .Where(e => e.EndedAt is not null)
            .Sum(e => (e.EndedAt!.Value - e.StartedAt).TotalSeconds);

        return Ok(new RunningTimerDto(
            running.TaskItemId, running.TaskItem.Title, running.StartedAt, tracked));
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
