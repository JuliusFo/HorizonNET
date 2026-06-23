using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HorizonNET.Data.Repositories;

public class TaskRepository(AppDbContext context) : ITaskRepository
{
    public async Task<IEnumerable<TaskItem>> GetAllAsync() =>
        await context.Tasks
            .Include(t => t.Project)
            .Where(t => t.ParentTaskId == null)
            .Include(t => t.SubTasks)
            .ToListAsync();

    public async Task<IEnumerable<TaskItem>> GetByProjectIdAsync(int projectId) =>
        await context.Tasks
            .Where(t => t.ProjectId == projectId && t.ParentTaskId == null)
            .Include(t => t.SubTasks)
            .ToListAsync();

    public async Task<IEnumerable<TaskItem>> GetInboxAsync() =>
        await context.Tasks
            .Where(t => t.ProjectId == null && t.ParentTaskId == null)
            .Include(t => t.SubTasks)
            .ToListAsync();

    public async Task<TaskItem?> GetByIdAsync(int id) =>
        await context.Tasks
            .Include(t => t.Project)
            .Include(t => t.SubTasks)
            .FirstOrDefaultAsync(t => t.Id == id);

    public async Task<TaskItem> CreateAsync(TaskItem task)
    {
        context.Tasks.Add(task);
        await context.SaveChangesAsync();
        return task;
    }

    public async Task<TaskItem?> UpdateAsync(int id, TaskItem updated)
    {
        var existing = await context.Tasks.FindAsync(id);
        if (existing is null) return null;

        existing.Title = updated.Title;
        existing.Description = updated.Description;
        existing.DueDate = updated.DueDate;
        existing.StartTime = updated.StartTime;
        existing.EndTime = updated.EndTime;
        existing.Status = updated.Status;
        existing.Priority = updated.Priority;
        existing.ProjectId = updated.ProjectId;
        await context.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var existing = await context.Tasks
            .Include(t => t.SubTasks)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (existing is null) return false;

        // Sub-Tasks manuell entfernen, da SQLite keine Cascade-Deletes auf
        // selbstreferenzierende Tabellen unterstützt
        context.Tasks.RemoveRange(existing.SubTasks);
        context.Tasks.Remove(existing);
        await context.SaveChangesAsync();
        return true;
    }
}
