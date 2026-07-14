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
