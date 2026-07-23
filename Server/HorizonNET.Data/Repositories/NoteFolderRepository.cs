using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HorizonNET.Data.Repositories;

public class NoteFolderRepository(AppDbContext context) : INoteFolderRepository
{
    // Flach geladen: Den Baum baut der Client, der ihn ohnehin mit den abgeleiteten
    // Ordnern zusammenführt. Sortierung nach Name – Ordner haben keine manuelle Ordnung.
    public async Task<IEnumerable<NoteFolder>> GetAllAsync() =>
        await context.NoteFolders.OrderBy(f => f.Name).ToListAsync();

    public async Task<NoteFolder?> GetByIdAsync(int id) =>
        await context.NoteFolders.FirstOrDefaultAsync(f => f.Id == id);

    public async Task<NoteFolder> CreateAsync(NoteFolder folder)
    {
        folder.CreatedAt = DateTime.Now;
        context.NoteFolders.Add(folder);
        await context.SaveChangesAsync();
        return folder;
    }

    public async Task<NoteFolder?> RenameAsync(int id, string name)
    {
        var existing = await context.NoteFolders.FindAsync(id);
        if (existing is null) return null;

        existing.Name = name;
        await context.SaveChangesAsync();
        return existing;
    }

    public async Task<NoteFolder?> MoveAsync(int id, int? newParentId)
    {
        var existing = await context.NoteFolders.FindAsync(id);
        if (existing is null) return null;

        // Ein Ordner darf weder unter sich selbst noch unter einen eigenen Nachfahren –
        // sonst hinge der Teilbaum in einem Ring und wäre von der Wurzel aus unerreichbar.
        if (newParentId == id) return null;
        if (newParentId is int target && await IsDescendantAsync(target, id)) return null;

        existing.ParentFolderId = newParentId;
        await context.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var existing = await context.NoteFolders.FindAsync(id);
        if (existing is null || existing.DeletedAt is not null) return false;

        // Soft-Delete inkl. aller Unterordner mit DEMSELBEN Zeitstempel – damit holt Undo
        // genau diese Menge zurück (Muster wie Projekt → Tasks).
        var now = DateTime.Now;
        foreach (var folder in await SubtreeAsync(id))
            folder.DeletedAt = now;

        await context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RestoreAsync(int id)
    {
        var existing = await context.NoteFolders
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(f => f.Id == id);
        if (existing is null || existing.DeletedAt is null) return false;

        var deletedAt = existing.DeletedAt;

        // Nur die im selben Vorgang mitgelöschten Unterordner zurückholen; vorher
        // eigenständig gelöschte bleiben gelöscht.
        var alle = await context.NoteFolders.IgnoreQueryFilters().ToListAsync();
        foreach (var folder in Subtree(alle, id).Where(f => f.DeletedAt == deletedAt))
            folder.DeletedAt = null;

        // Hängt der Ordner unter einem noch gelöschten Eltern-Ordner, käme er sonst
        // nicht zum Vorschein – dann an die oberste Ebene holen.
        var parent = existing.ParentFolderId is int pid
            ? alle.FirstOrDefault(f => f.Id == pid)
            : null;
        if (parent is { DeletedAt: not null }) existing.ParentFolderId = null;

        await context.SaveChangesAsync();
        return true;
    }

    public async Task<IEnumerable<NoteFolder>> GetDeletedAsync()
    {
        var alle = await context.NoteFolders.IgnoreQueryFilters().ToListAsync();

        // Nur eigenständig gelöschte Wurzeln: Ein Unterordner, der im selben Vorgang
        // (gleicher Zeitstempel) mit seinem Eltern-Ordner ging, kommt beim
        // Wiederherstellen von dort automatisch mit.
        bool KamMitEltern(NoteFolder f) =>
            f.ParentFolderId is int pid
            && alle.FirstOrDefault(p => p.Id == pid) is { DeletedAt: not null } parent
            && parent.DeletedAt == f.DeletedAt;

        return alle
            .Where(f => f.DeletedAt is not null && !KamMitEltern(f))
            .OrderByDescending(f => f.DeletedAt)
            .ToList();
    }

    public async Task<bool> PurgeAsync(int id)
    {
        var alle = await context.NoteFolders.IgnoreQueryFilters().ToListAsync();
        var existing = alle.FirstOrDefault(f => f.Id == id);
        if (existing is null || existing.DeletedAt is null) return false;

        // Unterordner explizit mitentfernen: Der Selbst-Fremdschlüssel ist NoAction,
        // ein DB-Cascade gibt es hier also nicht. Die Notizen darin bleiben bestehen –
        // ihre NoteFolderId räumt der Fremdschlüssel per SetNull ab.
        context.NoteFolders.RemoveRange(Subtree(alle, id));
        await context.SaveChangesAsync();
        return true;
    }

    // ── Baum-Hilfen ──────────────────────────────────────────────────────────

    // Ordner samt allen Nachfahren (aktive Sicht).
    private async Task<List<NoteFolder>> SubtreeAsync(int rootId)
    {
        var alle = await context.NoteFolders.ToListAsync();
        return Subtree(alle, rootId);
    }

    // In-Memory statt rekursivem SQL: Ordnerbäume sind klein, und SQLite kennt keine
    // rekursiven Navigationen über EF.
    private static List<NoteFolder> Subtree(List<NoteFolder> alle, int rootId)
    {
        var ergebnis = new List<NoteFolder>();
        var root = alle.FirstOrDefault(f => f.Id == rootId);
        if (root is null) return ergebnis;

        var offen = new Queue<NoteFolder>();
        offen.Enqueue(root);

        while (offen.Count > 0)
        {
            var current = offen.Dequeue();
            ergebnis.Add(current);

            foreach (var child in alle.Where(f => f.ParentFolderId == current.Id))
                if (!ergebnis.Contains(child))
                    offen.Enqueue(child);
        }

        return ergebnis;
    }

    // Ist "candidate" ein Nachfahre von "rootId"?
    private async Task<bool> IsDescendantAsync(int candidate, int rootId)
    {
        var alle = await context.NoteFolders.ToListAsync();
        return Subtree(alle, rootId).Any(f => f.Id == candidate && f.Id != rootId);
    }
}
