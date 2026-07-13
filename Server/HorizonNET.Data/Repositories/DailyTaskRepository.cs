using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HorizonNET.Data.Repositories;

public class DailyTaskRepository(AppDbContext context) : IDailyTaskRepository
{
    private IQueryable<DailyTask> WithIncludes() =>
        context.DailyTasks.Include(t => t.Project).Include(t => t.Completions);

    public async Task<IEnumerable<DailyTask>> GetAllAsync() =>
        await WithIncludes().OrderBy(t => t.SortOrder).ToListAsync();

    public async Task<IEnumerable<DailyTask>> GetActiveAsync() =>
        await WithIncludes().Where(t => t.IsActive).OrderBy(t => t.SortOrder).ToListAsync();

    public async Task<DailyTask?> GetByIdAsync(int id) =>
        await WithIncludes().FirstOrDefaultAsync(t => t.Id == id);

    public async Task<DailyTask> CreateAsync(DailyTask task)
    {
        // Neue Dailies ans Ende der Liste.
        var maxOrder = await context.DailyTasks.MaxAsync(t => (int?)t.SortOrder) ?? -1;
        task.SortOrder = maxOrder + 1;
        context.DailyTasks.Add(task);
        await context.SaveChangesAsync();
        return await GetByIdAsync(task.Id) ?? task;
    }

    public async Task<DailyTask?> UpdateAsync(int id, DailyTask updated)
    {
        var existing = await context.DailyTasks.FindAsync(id);
        if (existing is null) return null;

        existing.Title = updated.Title;
        existing.IsActive = updated.IsActive;
        existing.ProjectId = updated.ProjectId;
        existing.WeekdayMask = updated.WeekdayMask;
        await context.SaveChangesAsync();
        return await GetByIdAsync(id);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var existing = await context.DailyTasks.FindAsync(id);
        if (existing is null) return false;

        // Completions werden per Cascade mitgelöscht.
        context.DailyTasks.Remove(existing);
        await context.SaveChangesAsync();
        return true;
    }

    public async Task ReorderAsync(IList<int> orderedIds)
    {
        var tasks = await context.DailyTasks
            .Where(t => orderedIds.Contains(t.Id))
            .ToListAsync();

        foreach (var t in tasks)
            t.SortOrder = orderedIds.IndexOf(t.Id);

        await context.SaveChangesAsync();
    }

    public async Task<bool> SetCompletionAsync(int dailyTaskId, DateOnly date, bool completed)
    {
        var exists = await context.DailyTasks.AnyAsync(t => t.Id == dailyTaskId);
        if (!exists) return false;

        var existing = await context.DailyTaskCompletions
            .FirstOrDefaultAsync(c => c.DailyTaskId == dailyTaskId && c.Date == date);

        if (completed && existing is null)
            context.DailyTaskCompletions.Add(new DailyTaskCompletion { DailyTaskId = dailyTaskId, Date = date });
        else if (!completed && existing is not null)
            context.DailyTaskCompletions.Remove(existing);
        else
            return true; // schon im gewünschten Zustand

        await context.SaveChangesAsync();
        return true;
    }
}
