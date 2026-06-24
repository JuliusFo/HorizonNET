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
        if (existing is null) return false;

        context.Workspaces.Remove(existing);
        await context.SaveChangesAsync();
        return true;
    }
}
