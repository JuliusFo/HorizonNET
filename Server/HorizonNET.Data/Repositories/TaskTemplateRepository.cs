using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HorizonNET.Data.Repositories;

public class TaskTemplateRepository(AppDbContext context) : ITaskTemplateRepository
{
    private IQueryable<TaskTemplate> WithIncludes() =>
        context.TaskTemplates.Include(t => t.Project);

    public async Task<IEnumerable<TaskTemplate>> GetAllAsync() =>
        await WithIncludes().OrderBy(t => t.SortOrder).ToListAsync();

    public async Task<TaskTemplate?> GetByIdAsync(int id) =>
        await WithIncludes().FirstOrDefaultAsync(t => t.Id == id);

    public async Task<TaskTemplate> CreateAsync(TaskTemplate template)
    {
        // Neue Vorlagen ans Ende der Liste.
        var maxOrder = await context.TaskTemplates.MaxAsync(t => (int?)t.SortOrder) ?? -1;
        template.SortOrder = maxOrder + 1;
        context.TaskTemplates.Add(template);
        await context.SaveChangesAsync();
        return await GetByIdAsync(template.Id) ?? template;
    }

    public async Task<TaskTemplate?> UpdateAsync(int id, TaskTemplate updated)
    {
        var existing = await context.TaskTemplates.FindAsync(id);
        if (existing is null) return null;

        existing.Title = updated.Title;
        existing.Description = updated.Description;
        existing.Priority = updated.Priority;
        existing.ProjectId = updated.ProjectId;
        await context.SaveChangesAsync();
        return await GetByIdAsync(id);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var existing = await context.TaskTemplates.FindAsync(id);
        if (existing is null || existing.DeletedAt is not null) return false;

        existing.DeletedAt = DateTime.Now;
        await context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RestoreAsync(int id)
    {
        var existing = await context.TaskTemplates
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == id);
        if (existing is null || existing.DeletedAt is null) return false;

        existing.DeletedAt = null;
        await context.SaveChangesAsync();
        return true;
    }
}
