using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HorizonNET.Data.Repositories;

public class WorkspaceRepository(AppDbContext context) : IWorkspaceRepository
{
    public async Task<IEnumerable<Workspace>> GetAllAsync() =>
        await context.Workspaces.Include(w => w.Projects).ToListAsync();

    public async Task<Workspace?> GetByIdAsync(int id) =>
        await context.Workspaces.Include(w => w.Projects).FirstOrDefaultAsync(w => w.Id == id);

    public async Task<Workspace> CreateAsync(Workspace workspace)
    {
        context.Workspaces.Add(workspace);
        await context.SaveChangesAsync();
        return workspace;
    }

    public async Task<Workspace?> UpdateAsync(int id, Workspace updated)
    {
        var existing = await context.Workspaces.FindAsync(id);
        if (existing is null) return null;

        existing.Name = updated.Name;
        existing.Description = updated.Description;
        existing.Color = updated.Color;
        await context.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var existing = await context.Workspaces.FindAsync(id);
        if (existing is null || existing.DeletedAt is not null) return false;

        // Soft-Delete: nur den Arbeitsbereich stempeln. Zugeordnete Projekte
        // behalten ihre WorkspaceId (erscheinen ungruppiert), damit Undo die
        // Gruppierung ohne Weiteres wiederherstellt.
        existing.DeletedAt = DateTime.Now;
        await context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RestoreAsync(int id)
    {
        var existing = await context.Workspaces
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(w => w.Id == id);
        if (existing is null || existing.DeletedAt is null) return false;

        existing.DeletedAt = null;
        await context.SaveChangesAsync();
        return true;
    }

    public async Task<IEnumerable<Workspace>> GetDeletedAsync() =>
        await context.Workspaces
            .IgnoreQueryFilters()
            .Where(w => w.DeletedAt != null)
            .OrderByDescending(w => w.DeletedAt)
            .ToListAsync();

    public async Task<bool> PurgeAsync(int id)
    {
        var existing = await context.Workspaces
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(w => w.Id == id);
        if (existing is null || existing.DeletedAt is null) return false;

        // Projekte behalten hier ihre Daten – ihre WorkspaceId wird per FK auf null
        // gesetzt (SetNull), sie erscheinen danach ungruppiert.
        context.Workspaces.Remove(existing);
        await context.SaveChangesAsync();
        return true;
    }
}
