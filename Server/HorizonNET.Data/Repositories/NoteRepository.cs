using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using HorizonNET.Shared.Transfer;
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

    // Gesucht wird im sichtbaren Text, nicht im Markup – dafür muss der Inhalt aus der
    // Datenbank heraus. Derselbe zweistufige Weg wie im JournalRepository, nur aus einem
    // anderen Grund: Dort erzwingt ihn die Verschlüsselung, hier das HTML.
    //
    // Ein LIKE über die Content-Spalte kann das prinzipiell nicht leisten und lag in
    // BEIDE Richtungen daneben: „span" oder „style" trafen das Markup statt des Inhalts,
    // und „mit Anna" fand nichts, sobald zwischen den Wörtern ein </p><p> stand.
    //
    // Alles zu laden ist hier bezahlbar (die gesamte Datenbank liegt im dreistelligen
    // KB-Bereich). Sollte der Notizbestand je so wachsen, dass es sich lohnt, wäre der
    // nächste Schritt eine mitgeschriebene Klartextspalte – nicht vorher.
    public async Task<IEnumerable<Note>> SearchAsync(string query, int limit)
    {
        // Zeichnungen bleiben vollständig in SQL: Sie werden ohnehin nur über den Titel
        // gesucht (ein Treffer auf „path" oder „stroke" im SVG wäre Unsinn), und so wird
        // ihr Content – der größte Brocken im Bestand – gar nicht erst geladen.
        var pattern = SearchPattern.For(query);
        var drawings = await WithIncludes()
            .Where(n => n.Kind == NoteKind.Drawing
                     && EF.Functions.Like(n.Title, pattern, SearchPattern.Escape))
            .ToListAsync();

        // HTML-Notizen: laden, Markup entfernen, dann prüfen.
        var htmlNotes = await WithIncludes()
            .Where(n => n.Kind == NoteKind.Html)
            .ToListAsync();

        return drawings
            .Concat(htmlNotes.Where(n => Matches(n, query)))
            .OrderByDescending(n => n.UpdatedAt)
            .Take(limit)
            .ToList();
    }

    // Teilstring-Vergleich wie zuvor und wie bei Tasks und Projekten – nur eben über den
    // Klartext. Die Trefferregel der Palette bleibt damit für alle Kategorien dieselbe.
    // NoteSnippet ist dieselbe Umwandlung, die auch die Vorschau in der Liste erzeugt:
    // Was dort zu lesen ist, ist genau das, was hier durchsucht wird.
    private static bool Matches(Note note, string query) =>
        note.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || NoteSnippet.From(note.Content, int.MaxValue)
                      .Contains(query, StringComparison.CurrentCultureIgnoreCase);

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
