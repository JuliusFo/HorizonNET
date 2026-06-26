using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using HorizonNET.Shared.Transfer.Enums;
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

    public async Task SetGoogleEventIdAsync(int taskId, string? googleEventId)
    {
        var existing = await context.Tasks.FindAsync(taskId);
        if (existing is null) return;

        existing.GoogleEventId = googleEventId;
        await context.SaveChangesAsync();
    }

    public async Task ReorderAsync(WorkStatus status, IList<int> orderedTaskIds)
    {
        var tasks = await context.Tasks
            .Where(t => orderedTaskIds.Contains(t.Id))
            .ToListAsync();

        foreach (var t in tasks)
        {
            t.SortOrder = orderedTaskIds.IndexOf(t.Id);
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

        // Sub-Tasks manuell entfernen, da SQLite keine Cascade-Deletes auf
        // selbstreferenzierende Tabellen unterstützt
        context.Tasks.RemoveRange(existing.SubTasks);
        context.Tasks.Remove(existing);
        await context.SaveChangesAsync();
        return true;
    }
}
