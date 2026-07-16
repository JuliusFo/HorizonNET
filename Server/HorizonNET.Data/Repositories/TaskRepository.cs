using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using HorizonNET.Shared.Transfer.Enums;
using Microsoft.EntityFrameworkCore;

namespace HorizonNET.Data.Repositories;

public class TaskRepository(AppDbContext context) : ITaskRepository
{
    // Zeiten werden überall mitgeladen: die Antwort-DTOs melden erfasste und laufende
    // Zeit an jedem Task (inkl. Sub-Tasks), damit jede Ansicht die Uhr zeigen kann.
    public async Task<IEnumerable<TaskItem>> GetAllAsync() =>
        await context.Tasks
            .Include(t => t.Project)
            .Where(t => t.ParentTaskId == null)
            .Include(t => t.TimeEntries)
            .Include(t => t.SubTasks).ThenInclude(s => s.TimeEntries)
            .ToListAsync();

    public async Task<IEnumerable<TaskItem>> GetByProjectIdAsync(int projectId) =>
        await context.Tasks
            .Where(t => t.ProjectId == projectId && t.ParentTaskId == null)
            .Include(t => t.TimeEntries)
            .Include(t => t.SubTasks).ThenInclude(s => s.TimeEntries)
            .ToListAsync();

    public async Task<IEnumerable<TaskItem>> GetInboxAsync() =>
        await context.Tasks
            .Where(t => t.ProjectId == null && t.ParentTaskId == null)
            .Include(t => t.TimeEntries)
            .Include(t => t.SubTasks).ThenInclude(s => s.TimeEntries)
            .ToListAsync();

    public async Task<TaskItem?> GetByIdAsync(int id) =>
        await context.Tasks
            .Include(t => t.Project)
            .Include(t => t.TimeEntries)
            .Include(t => t.SubTasks).ThenInclude(s => s.TimeEntries)
            .FirstOrDefaultAsync(t => t.Id == id);

    public async Task<TaskItem> CreateAsync(TaskItem task)
    {
        var now = DateTime.Now;
        task.CreatedAt = now;
        task.UpdatedAt = now;

        // Auch ein direkt als "Geplant Heute" angelegter Task ist heute fällig – sonst
        // gälte die Invariante je nach Einstiegspunkt unterschiedlich. Kein Client nutzt
        // das aktuell (alle legen mit "Geplant" an), die API erlaubt es aber.
        ApplyDueDateForStatusChange(task, WorkStatus.Planned, task.Status);

        context.Tasks.Add(task);
        await context.SaveChangesAsync();
        return task;
    }

    public async Task<TaskItem?> UpdateAsync(int id, TaskItem updated)
    {
        var existing = await context.Tasks.FindAsync(id);
        if (existing is null) return null;

        var previousStatus = existing.Status;

        existing.Title = updated.Title;
        existing.Description = updated.Description;
        existing.DueDate = updated.DueDate;
        existing.StartTime = updated.StartTime;
        existing.EndTime = updated.EndTime;
        existing.Status = updated.Status;
        existing.Priority = updated.Priority;
        existing.ProjectId = updated.ProjectId;
        existing.UpdatedAt = DateTime.Now;

        ApplyDueDateForStatusChange(existing, previousStatus, updated.Status);
        await ApplyTimerForStatusChangeAsync(id, previousStatus, updated.Status);

        await context.SaveChangesAsync();
        return await GetByIdAsync(id) ?? existing;
    }

    // "Geplant Heute" bedeutet: heute fällig – also auch das Fälligkeitsdatum setzen.
    // Ohne das erschiene der Task nie auf der Heute-Seite, denn die filtert nach DueDate
    // und kennt den Status nicht. Bewusst hier und nicht im Client (wie die Timer-
    // Steuerung), damit es für Kanban-Board, Dialog und Detailseite gleichermaßen gilt.
    // Nur beim WECHSEL in den Status: sonst würde jede spätere Bearbeitung eines schon
    // länger geplanten Tasks sein Datum wieder auf heute ziehen.
    private static void ApplyDueDateForStatusChange(TaskItem task, WorkStatus previous, WorkStatus current)
    {
        if (current == WorkStatus.PlannedToday && previous != WorkStatus.PlannedToday)
            task.DueDate = DateTime.Today;
    }

    // Der Status steuert die Zeiterfassung: "In Arbeit" startet den Timer, jeder
    // Wechsel weg davon (Pausiert, Fertig, Abgebrochen, zurück auf Geplant) stoppt ihn.
    // Bewusst hier und nicht im Client, damit es für Liste, Kanban-Board, Dialog und
    // Detailseite gleichermaßen gilt. Gespeichert wird vom Aufrufer.
    private async Task ApplyTimerForStatusChangeAsync(int taskId, WorkStatus previous, WorkStatus current)
    {
        if (previous == current) return;

        if (current == WorkStatus.InProgress)
        {
            // Höchstens ein laufender Timer: einen woanders laufenden zuerst stoppen.
            var now = DateTime.Now;
            var running = await context.TimeEntries.FirstOrDefaultAsync(t => t.EndedAt == null);
            if (running is not null)
            {
                if (running.TaskItemId == taskId) return; // läuft bereits
                running.EndedAt = now;

                // Der verdrängte Task ist nicht mehr in Arbeit – sein Status muss das
                // spiegeln, sonst stünden zwei Tasks auf "In Arbeit" und nur einer liefe.
                var displaced = await context.Tasks.FindAsync(running.TaskItemId);
                if (displaced is not null && displaced.Status == WorkStatus.InProgress)
                {
                    displaced.Status = WorkStatus.Paused;
                    displaced.UpdatedAt = now;
                }
            }

            context.TimeEntries.Add(new TimeEntry { TaskItemId = taskId, StartedAt = now });
        }
        else if (previous == WorkStatus.InProgress)
        {
            var running = await context.TimeEntries
                .FirstOrDefaultAsync(t => t.TaskItemId == taskId && t.EndedAt == null);
            if (running is not null) running.EndedAt = DateTime.Now;
        }
    }

    public async Task SetGoogleEventIdAsync(int taskId, string? googleEventId)
    {
        var existing = await context.Tasks.FindAsync(taskId);
        if (existing is null) return;

        existing.GoogleEventId = googleEventId;
        await context.SaveChangesAsync();
    }

    public async Task<HashSet<string>> GetGoogleEventIdsAsync()
    {
        var ids = await context.Tasks
            .Where(t => t.GoogleEventId != null)
            .Select(t => t.GoogleEventId!)
            .ToListAsync();
        return ids.ToHashSet();
    }

    public async Task ReorderAsync(WorkStatus status, IList<int> orderedTaskIds)
    {
        var tasks = await context.Tasks
            .Where(t => orderedTaskIds.Contains(t.Id))
            .ToListAsync();

        foreach (var t in tasks)
        {
            t.SortOrder = orderedTaskIds.IndexOf(t.Id);

            // Auch das Verschieben im Kanban-Board ist ein Statuswechsel und steuert
            // damit den Timer (Spalte "In Arbeit" startet, jede andere stoppt) sowie
            // das Fälligkeitsdatum (Spalte "Geplant Heute" setzt es auf heute).
            await ApplyTimerForStatusChangeAsync(t.Id, t.Status, status);
            ApplyDueDateForStatusChange(t, t.Status, status);
            t.Status = status;
        }
        await context.SaveChangesAsync();
    }

    public async Task ReorderSubTasksAsync(IList<int> orderedTaskIds)
    {
        var tasks = await context.Tasks
            .Where(t => orderedTaskIds.Contains(t.Id))
            .ToListAsync();

        // Nur die Reihenfolge ändern – der Status der Sub-Tasks bleibt erhalten.
        foreach (var t in tasks)
            t.SortOrder = orderedTaskIds.IndexOf(t.Id);

        await context.SaveChangesAsync();
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var existing = await context.Tasks
            .Include(t => t.SubTasks)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (existing is null) return false;

        // Soft-Delete: Task und seine (aktiven) Sub-Tasks mit demselben
        // Zeitstempel stempeln, damit Undo genau diese Menge wiederherstellt.
        var now = DateTime.Now;
        existing.DeletedAt = now;
        foreach (var sub in existing.SubTasks)
            sub.DeletedAt = now;

        // Ein laufender Timer am gelöschten Task (oder Sub-Task) würde sonst ewig
        // weiterlaufen und jeden weiteren Start blockieren.
        var affectedIds = existing.SubTasks.Select(s => s.Id).Append(id).ToList();
        var runningEntries = await context.TimeEntries
            .Where(t => t.EndedAt == null && affectedIds.Contains(t.TaskItemId))
            .ToListAsync();
        foreach (var entry in runningEntries)
            entry.EndedAt = now;

        await context.SaveChangesAsync();
        return true;
    }

    public async Task<IEnumerable<TaskItem>> SearchAsync(string query, int limit)
    {
        var pattern = SearchPattern.For(query);
        return await context.Tasks
            .Include(t => t.Project)
            .Where(t => EF.Functions.Like(t.Title, pattern, SearchPattern.Escape)
                     || (t.Description != null
                         && EF.Functions.Like(t.Description, pattern, SearchPattern.Escape)))
            .OrderByDescending(t => t.UpdatedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<bool> RestoreAsync(int id)
    {
        var existing = await context.Tasks
            .IgnoreQueryFilters()
            .Include(t => t.SubTasks)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (existing is null || existing.DeletedAt is null) return false;

        var deletedAt = existing.DeletedAt;
        existing.DeletedAt = null;
        // Nur die im selben Vorgang gelöschten Sub-Tasks zurückholen (gleicher
        // Zeitstempel) – vorher unabhängig gelöschte bleiben gelöscht.
        foreach (var sub in existing.SubTasks.Where(s => s.DeletedAt == deletedAt))
            sub.DeletedAt = null;
        await context.SaveChangesAsync();
        return true;
    }

    public async Task<IEnumerable<TaskItem>> GetDeletedAsync()
    {
        var deleted = await context.Tasks
            .IgnoreQueryFilters()
            .Where(t => t.DeletedAt != null)
            .Include(t => t.Project)
            .Include(t => t.ParentTask)
            .OrderByDescending(t => t.DeletedAt)
            .ToListAsync();

        // Nur eigenständig gelöschte "Wurzeln": Tasks, die im selben Vorgang (gleicher
        // Zeitstempel) mit ihrem Projekt oder Eltern-Task gelöscht wurden, kämen beim
        // Wiederherstellen von dort automatisch mit zurück – hier also ausblenden.
        static bool CameWithParent(TaskItem t) =>
            (t.Project is { DeletedAt: not null } p && p.DeletedAt == t.DeletedAt)
            || (t.ParentTask is { DeletedAt: not null } pt && pt.DeletedAt == t.DeletedAt);

        return deleted.Where(t => !CameWithParent(t)).ToList();
    }

    public async Task<bool> PurgeAsync(int id)
    {
        var existing = await context.Tasks
            .IgnoreQueryFilters()
            .Include(t => t.SubTasks)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (existing is null || existing.DeletedAt is null) return false;

        // Sub-Tasks manuell entfernen (FK ParentTask ist NoAction, kein DB-Cascade);
        // die Zeiten von Haupt- und Sub-Tasks gehen per FK-Cascade mit, Notizen per SetNull.
        if (existing.SubTasks.Count > 0)
            context.Tasks.RemoveRange(existing.SubTasks);
        context.Tasks.Remove(existing);
        await context.SaveChangesAsync();
        return true;
    }
}
