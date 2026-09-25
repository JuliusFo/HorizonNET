using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HorizonNET.Data.Repositories;

public class TaskSeriesRepository(AppDbContext context) : ITaskSeriesRepository
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    private IQueryable<TaskSeries> WithIncludes() =>
        context.TaskSeries.Include(s => s.Project).Include(s => s.Slots);

    public async Task<IEnumerable<TaskSeries>> GetAllAsync() =>
        await WithIncludes().OrderBy(s => s.Title).ToListAsync();

    public async Task<TaskSeries?> GetByIdAsync(int id) =>
        await WithIncludes().FirstOrDefaultAsync(s => s.Id == id);

    public async Task<TaskSeries> CreateAsync(TaskSeries series)
    {
        var now = DateTime.Now;
        series.CreatedAt = now;
        series.UpdatedAt = now;

        context.TaskSeries.Add(series);
        await context.SaveChangesAsync();
        return await GetByIdAsync(series.Id) ?? series;
    }

    public async Task<TaskSeriesChange?> UpdateAsync(int id, TaskSeries updated)
    {
        var existing = await context.TaskSeries
            .Include(s => s.Slots)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (existing is null) return null;

        var now = DateTime.Now;

        existing.Title           = updated.Title;
        existing.Description     = updated.Description;
        existing.Kind            = updated.Kind;
        existing.Priority        = updated.Priority;
        existing.ReminderMinutes = updated.ReminderMinutes;
        existing.IsActive        = updated.IsActive;
        existing.ProjectId       = updated.ProjectId;
        existing.UpdatedAt       = now;

        var removedSlotIds = SyncSlots(existing, updated.Slots);

        // Zukünftige Termine nachziehen. Nur die noch offenen: Ein bereits als "war"/
        // "abgesagt" markierter Termin ist Historie, auch wenn er in der Zukunft liegt.
        var future = await FutureTasksAsync(id, Today);
        var removed  = new List<TaskItem>();
        var upserted = new List<TaskItem>();

        foreach (var task in future)
        {
            if (task.SeriesSlotId is int slotId && removedSlotIds.Contains(slotId))
            {
                task.DeletedAt = now;
                removed.Add(task);
                continue;
            }

            if (task.IsCompleted) continue;

            task.Title           = existing.Title;
            task.Description     = existing.Description;
            task.ProjectId       = existing.ProjectId;
            task.Kind            = existing.Kind;
            task.Priority        = existing.Priority;
            task.ReminderMinutes = existing.ReminderMinutes;

            // Uhrzeit nur, wenn der Termin noch auf seinem nominellen Tag liegt – ein
            // bewusst verlegter Einzeltermin behält seine Zeit.
            var slot = task.SeriesSlotId is int sid ? existing.Slots.FirstOrDefault(s => s.Id == sid) : null;
            if (slot is not null && task.DueDate?.Date == task.SeriesDate?.ToDateTime(TimeOnly.MinValue))
            {
                task.StartTime = task.DueDate.Value.Date.Add(slot.StartTime.ToTimeSpan());
                task.EndTime   = task.DueDate.Value.Date.Add(slot.EndTime.ToTimeSpan());
            }

            task.UpdatedAt = now;
            upserted.Add(task);
        }

        await context.SaveChangesAsync();
        return new TaskSeriesChange((await GetByIdAsync(id))!, upserted, removed);
    }

    // Slots abgleichen statt "alle löschen, alle neu": Die Ids bleiben stabil, damit die
    // materialisierten Termine (TaskItem.SeriesSlotId) ihren Slot behalten. Liefert die
    // Ids der entfernten Slots – deren zukünftige Termine räumt UpdateAsync ab.
    private HashSet<int> SyncSlots(TaskSeries existing, ICollection<TaskSeriesSlot> wanted)
    {
        var keep = new HashSet<int>();

        foreach (var slot in wanted)
        {
            var match = slot.Id > 0 ? existing.Slots.FirstOrDefault(s => s.Id == slot.Id) : null;
            if (match is null)
            {
                match = new TaskSeriesSlot { SeriesId = existing.Id };
                existing.Slots.Add(match);
            }

            match.DayOfWeek = slot.DayOfWeek;
            match.StartTime = slot.StartTime;
            match.EndTime   = slot.EndTime;
            keep.Add(match.Id);
        }

        // Neue Slots haben noch Id 0 und stehen nicht in keep – die dürfen nicht gleich
        // wieder rausfliegen, deshalb Id > 0 als Bedingung.
        var gone = existing.Slots.Where(s => s.Id > 0 && !keep.Contains(s.Id)).ToList();
        foreach (var slot in gone)
            context.TaskSeriesSlots.Remove(slot);

        return gone.Select(s => s.Id).ToHashSet();
    }

    public async Task<IReadOnlyList<TaskItem>> MaterializeAsync(DateOnly from, DateOnly to)
    {
        if (to < from) return [];

        var series = await context.TaskSeries
            .Include(s => s.Slots)
            .Where(s => s.IsActive && s.Slots.Count > 0)
            .ToListAsync();
        if (series.Count == 0) return [];

        // Was es schon gibt – inklusive soft-gelöschter Zeilen. Ein abgesagter (gelöschter)
        // Termin darf beim nächsten Lauf nicht wiederkommen.
        var seriesIds = series.Select(s => s.Id).ToList();
        var existing = (await context.Tasks
                .IgnoreQueryFilters()
                .Where(t => t.SeriesId != null && seriesIds.Contains(t.SeriesId.Value)
                         && t.SeriesDate >= from && t.SeriesDate <= to)
                .Select(t => new { t.SeriesId, t.SeriesSlotId, t.SeriesDate })
                .ToListAsync())
            .Select(x => (x.SeriesId!.Value, x.SeriesSlotId, x.SeriesDate))
            .ToHashSet();

        var now = DateTime.Now;
        var created = new List<TaskItem>();

        foreach (var s in series)
        {
            for (var day = from; day <= to; day = day.AddDays(1))
            {
                foreach (var slot in s.Slots.Where(x => x.DayOfWeek == day.DayOfWeek))
                {
                    if (existing.Contains((s.Id, slot.Id, day))) continue;

                    var date = day.ToDateTime(TimeOnly.MinValue);
                    var task = new TaskItem
                    {
                        Title           = s.Title,
                        Description     = s.Description,
                        Kind            = s.Kind,
                        Priority        = s.Priority,
                        ReminderMinutes = s.ReminderMinutes,
                        ProjectId       = s.ProjectId,
                        DueDate         = date,
                        StartTime       = date.Add(slot.StartTime.ToTimeSpan()),
                        EndTime         = date.Add(slot.EndTime.ToTimeSpan()),
                        SeriesId        = s.Id,
                        SeriesSlotId    = slot.Id,
                        SeriesDate      = day,
                        CreatedAt       = now,
                        UpdatedAt       = now
                    };
                    context.Tasks.Add(task);
                    created.Add(task);
                }
            }
        }

        if (created.Count > 0) await context.SaveChangesAsync();
        return created;
    }

    public async Task<TaskSeriesChange?> DeleteAsync(int id)
    {
        var existing = await WithIncludes().FirstOrDefaultAsync(s => s.Id == id);
        if (existing is null || existing.DeletedAt is not null) return null;

        // Serie und zukünftige Termine mit demselben Zeitstempel stempeln, damit Undo
        // genau diese Menge zurückholt. Vergangene Termine bleiben als Historie stehen.
        var now = DateTime.Now;
        existing.DeletedAt = now;

        var removed = await FutureTasksAsync(id, Today);
        foreach (var task in removed)
            task.DeletedAt = now;

        await context.SaveChangesAsync();
        return new TaskSeriesChange(existing, [], removed);
    }

    public async Task<TaskSeriesChange?> RestoreAsync(int id)
    {
        var existing = await context.TaskSeries
            .IgnoreQueryFilters()
            .Include(s => s.Project)
            .Include(s => s.Slots)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (existing is null || existing.DeletedAt is null) return null;

        var stamp = existing.DeletedAt;
        existing.DeletedAt = null;

        // Nur die im selben Vorgang gelöschten Termine (gleicher Zeitstempel) zurückholen –
        // ein vorher einzeln abgesagter bleibt abgesagt.
        var restored = await context.Tasks
            .IgnoreQueryFilters()
            .Where(t => t.SeriesId == id && t.DeletedAt == stamp)
            .ToListAsync();
        foreach (var task in restored)
            task.DeletedAt = null;

        await context.SaveChangesAsync();
        return new TaskSeriesChange(existing, restored, []);
    }

    public async Task<IEnumerable<TaskSeries>> GetDeletedAsync() =>
        await context.TaskSeries
            .IgnoreQueryFilters()
            .Include(s => s.Project)
            .Include(s => s.Slots)
            .Where(s => s.DeletedAt != null)
            .OrderByDescending(s => s.DeletedAt)
            .ToListAsync();

    public async Task<bool> PurgeAsync(int id)
    {
        var existing = await context.TaskSeries
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == id);
        if (existing is null || existing.DeletedAt is null) return false;

        // Die mit der Serie gelöschten Termine gehen endgültig mit (sonst tauchten sie
        // nach dem SetNull als eigenständige Papierkorb-Einträge auf). Vergangene,
        // nicht gelöschte Termine verlieren per FK-SetNull nur den Bezug.
        var stamp = existing.DeletedAt;
        var tasks = await context.Tasks
            .IgnoreQueryFilters()
            .Where(t => t.SeriesId == id && t.DeletedAt == stamp)
            .ToListAsync();
        context.Tasks.RemoveRange(tasks);

        // Slots hängen per Cascade an der Serie.
        context.TaskSeries.Remove(existing);
        await context.SaveChangesAsync();
        return true;
    }

    // Aktive (nicht gelöschte) Termine der Serie ab einem nominellen Datum.
    private async Task<List<TaskItem>> FutureTasksAsync(int seriesId, DateOnly from) =>
        await context.Tasks
            .Where(t => t.SeriesId == seriesId && t.SeriesDate >= from)
            .ToListAsync();
}
