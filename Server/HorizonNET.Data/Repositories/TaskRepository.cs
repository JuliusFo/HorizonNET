using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HorizonNET.Data.Repositories;

public class TaskRepository(AppDbContext context) : ITaskRepository
{
    public async Task<IEnumerable<TaskItem>> GetAllAsync() =>
        await context.Tasks.Include(t => t.Project).ToListAsync();

    public async Task<IEnumerable<TaskItem>> GetByProjectIdAsync(int projectId) =>
        await context.Tasks.Where(t => t.ProjectId == projectId).ToListAsync();

    public async Task<TaskItem?> GetByIdAsync(int id) =>
        await context.Tasks.Include(t => t.Project).FirstOrDefaultAsync(t => t.Id == id);

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
        existing.IsCompleted = updated.IsCompleted;
        existing.Priority = updated.Priority;
        existing.ProjectId = updated.ProjectId;
        await context.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var existing = await context.Tasks.FindAsync(id);
        if (existing is null) return false;

        context.Tasks.Remove(existing);
        await context.SaveChangesAsync();
        return true;
    }
}
