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
        await context.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var existing = await context.Projects.FindAsync(id);
        if (existing is null) return false;

        context.Projects.Remove(existing);
        await context.SaveChangesAsync();
        return true;
    }
}
