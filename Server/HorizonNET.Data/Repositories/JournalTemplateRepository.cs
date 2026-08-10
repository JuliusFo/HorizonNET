using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HorizonNET.Data.Repositories;

public class JournalTemplateRepository(AppDbContext context) : IJournalTemplateRepository
{
    public async Task<IEnumerable<JournalTemplate>> GetAllAsync() =>
        await context.JournalTemplates
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Id)
            .ToListAsync();

    public async Task<JournalTemplate?> GetByIdAsync(int id) =>
        await context.JournalTemplates.FirstOrDefaultAsync(t => t.Id == id);

    public async Task<JournalTemplate> CreateAsync(JournalTemplate template)
    {
        // Ans Ende einsortieren statt auf 0 zu lassen (Lehre aus Phase 10b).
        var max = await context.JournalTemplates
            .Select(t => (int?)t.SortOrder)
            .MaxAsync() ?? -1;

        template.SortOrder = max + 1;
        template.CreatedAt = DateTime.Now;

        context.JournalTemplates.Add(template);
        await context.SaveChangesAsync();
        return template;
    }

    public async Task<JournalTemplate?> UpdateAsync(int id, JournalTemplate updated)
    {
        var existing = await context.JournalTemplates.FindAsync(id);
        if (existing is null) return null;

        existing.Name = updated.Name;
        existing.Content = updated.Content;
        existing.SortOrder = updated.SortOrder;

        await context.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var existing = await context.JournalTemplates.FindAsync(id);
        if (existing is null || existing.DeletedAt is not null) return false;

        existing.DeletedAt = DateTime.Now;
        await context.SaveChangesAsync();
        return true;
    }
}
