using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HorizonNET.Data.Repositories;

public class ProjectRepository(AppDbContext context) : IProjectRepository
{
    public async Task<IEnumerable<Project>> GetAllAsync() =>
        await context.Projects.Include(p => p.Tasks).ToListAsync();

    public async Task<Project?> GetByIdAsync(int id) =>
        await context.Projects.Include(p => p.Tasks).FirstOrDefaultAsync(p => p.Id == id);

    public async Task<Project> CreateAsync(Project project)
    {
        context.Projects.Add(project);
        await context.SaveChangesAsync();
        return project;
    }

    public async Task<Project?> UpdateAsync(int id, Project updated)
    {
        var existing = await context.Projects.FindAsync(id);
        if (existing is null) return null;

        existing.Name = updated.Name;
        existing.Description = updated.Description;
        existing.Status = updated.Status;
        existing.Priority = updated.Priority;
        existing.Color = updated.Color;
        existing.WorkspaceId = updated.WorkspaceId;
        await context.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var existing = await context.Projects.FindAsync(id);
        if (existing is null || existing.DeletedAt is not null) return false;

        // Soft-Delete inkl. Cascade: alle (aktiven) Tasks des Projekts mit demselben
        // Zeitstempel stempeln, damit Undo genau diese Menge wiederherstellt.
        var now = DateTime.Now;
        existing.DeletedAt = now;
        var tasks = await context.Tasks.Where(t => t.ProjectId == id).ToListAsync();
        foreach (var t in tasks)
            t.DeletedAt = now;
        await context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RestoreAsync(int id)
    {
        var existing = await context.Projects
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == id);
        if (existing is null || existing.DeletedAt is null) return false;

        var deletedAt = existing.DeletedAt;
        existing.DeletedAt = null;
        // Nur die im selben Vorgang mitgelöschten Tasks zurückholen (gleicher
        // Zeitstempel). In-Memory-Vergleich, um DateTime-Genauigkeit in SQL zu meiden.
        var tasks = await context.Tasks
            .IgnoreQueryFilters()
            .Where(t => t.ProjectId == id && t.DeletedAt != null)
            .ToListAsync();
        foreach (var t in tasks.Where(t => t.DeletedAt == deletedAt))
            t.DeletedAt = null;
        await context.SaveChangesAsync();
        return true;
    }
}
