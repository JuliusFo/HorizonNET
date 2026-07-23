using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using HorizonNET.Shared.Transfer.Enums;
using Microsoft.EntityFrameworkCore;

namespace HorizonNET.Data.Repositories;

public class NoteRepository(AppDbContext context) : INoteRepository
{
    // TaskItem/Project werden mitgeladen, damit das DTO Titel/Projektname für die
    // Liste liefern kann. Sortierung: zuletzt geändert zuerst.
    private IQueryable<Note> WithIncludes() =>
        context.Notes.Include(n => n.TaskItem).Include(n => n.Project);

    public async Task<IEnumerable<Note>> GetAllAsync() =>
        await WithIncludes().OrderByDescending(n => n.UpdatedAt).ToListAsync();

    public async Task<Note?> GetByIdAsync(int id) =>
        await WithIncludes().FirstOrDefaultAsync(n => n.Id == id);

    public async Task<IEnumerable<Note>> GetByTaskIdAsync(int taskId) =>
        await WithIncludes()
            .Where(n => n.TaskItemId == taskId)
            .OrderByDescending(n => n.UpdatedAt)
            .ToListAsync();

    public async Task<IEnumerable<Note>> GetByProjectIdAsync(int projectId) =>
        await WithIncludes()
            .Where(n => n.ProjectId == projectId)
            .OrderByDescending(n => n.UpdatedAt)
            .ToListAsync();

    public async Task<Note> CreateAsync(Note note)
    {
        var now = DateTime.Now;
        note.CreatedAt = now;
        note.UpdatedAt = now;
        context.Notes.Add(note);
        await context.SaveChangesAsync();
        // Erneut inkl. Navigationen laden, damit das DTO Task-/Projektname trägt.
        return await GetByIdAsync(note.Id) ?? note;
    }

    public async Task<Note?> UpdateAsync(int id, Note updated)
    {
        var existing = await context.Notes.FindAsync(id);
        if (existing is null) return null;

        existing.Title = updated.Title;
        existing.Content = updated.Content;
        existing.TaskItemId = updated.TaskItemId;
        existing.ProjectId = updated.ProjectId;
        existing.NoteFolderId = updated.NoteFolderId;
        existing.Thumbnail = updated.Thumbnail;
        // Kind bewusst NICHT übernehmen – die Art einer Notiz bleibt, wie sie angelegt wurde.
        existing.UpdatedAt = DateTime.Now;
        await context.SaveChangesAsync();
        return await GetByIdAsync(id);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var existing = await context.Notes.FindAsync(id);
        if (existing is null || existing.DeletedAt is not null) return false;

        existing.DeletedAt = DateTime.Now;
        await context.SaveChangesAsync();
        return true;
    }

    public async Task<IEnumerable<Note>> SearchAsync(string query, int limit)
    {
        var pattern = SearchPattern.For(query);
        return await WithIncludes()
            // Zeichnungen (Kind == Drawing) nur über den Titel durchsuchen – ein LIKE über
            // deren SVG-Content in Content würde bei „path", „stroke" o. Ä. Unsinn treffen.
            .Where(n => EF.Functions.Like(n.Title, pattern, SearchPattern.Escape)
                     || (n.Kind == NoteKind.Html
                         && EF.Functions.Like(n.Content, pattern, SearchPattern.Escape)))
            .OrderByDescending(n => n.UpdatedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<bool> RestoreAsync(int id)
    {
        var existing = await context.Notes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(n => n.Id == id);
        if (existing is null || existing.DeletedAt is null) return false;

        existing.DeletedAt = null;
        await context.SaveChangesAsync();
        return true;
    }

    public async Task<IEnumerable<Note>> GetDeletedAsync() =>
        await WithIncludes()
            .IgnoreQueryFilters()
            .Where(n => n.DeletedAt != null)
            .OrderByDescending(n => n.DeletedAt)
            .ToListAsync();

    public async Task<bool> PurgeAsync(int id)
    {
        var existing = await context.Notes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(n => n.Id == id);
        if (existing is null || existing.DeletedAt is null) return false;

        context.Notes.Remove(existing);
        await context.SaveChangesAsync();
        return true;
    }
}
