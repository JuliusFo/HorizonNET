using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HorizonNET.Data.Repositories;

public class TimeEntryRepository(AppDbContext context) : ITimeEntryRepository
{
    public async Task<IEnumerable<TimeEntry>> GetByTaskAsync(int taskId) =>
        await context.TimeEntries
            .Where(t => t.TaskItemId == taskId)
            .OrderByDescending(t => t.StartedAt)
            .ToListAsync();

    public async Task<TimeEntry?> GetRunningAsync() =>
        await context.TimeEntries
            .Include(t => t.TaskItem)
            .FirstOrDefaultAsync(t => t.EndedAt == null);

    // Alles, was den Tag überlappt: Start vor Tagesende UND Ende nach Tagesbeginn.
    // Ein noch laufendes Intervall (EndedAt == null) zählt bis jetzt.
    public async Task<IEnumerable<TimeEntry>> GetForDayAsync(DateOnly date)
    {
        var dayStart = date.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);
        var now = DateTime.Now;

        return await context.TimeEntries
            .Include(t => t.TaskItem)
            .Where(t => t.StartedAt < dayEnd && (t.EndedAt ?? now) > dayStart)
            .OrderBy(t => t.StartedAt)
            .ToListAsync();
    }

    public async Task<bool> StopAsync(int taskId)
    {
        var running = await context.TimeEntries
            .FirstOrDefaultAsync(t => t.TaskItemId == taskId && t.EndedAt == null);
        if (running is null) return false;

        running.EndedAt = DateTime.Now;
        await context.SaveChangesAsync();
        return true;
    }
}
