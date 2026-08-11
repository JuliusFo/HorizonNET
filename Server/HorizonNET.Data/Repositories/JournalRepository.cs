using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using HorizonNET.Shared.Transfer;
using Microsoft.EntityFrameworkCore;

namespace HorizonNET.Data.Repositories;

public class JournalRepository(AppDbContext context) : IJournalRepository
{
    // Project/TaskItem für die Anzeige der Verknüpfung, Moods für den Tages-Zeitstrahl.
    // Die Stimmungen kommen chronologisch, damit die Anzeige nicht nachsortieren muss.
    private IQueryable<JournalEntry> WithIncludes() =>
        context.JournalEntries
            .Include(j => j.Project)
            .Include(j => j.TaskItem)
            .Include(j => j.Moods.OrderBy(m => m.RecordedAt));

    public async Task<JournalEntry?> GetByDateAsync(DateOnly date) =>
        await WithIncludes().FirstOrDefaultAsync(j => j.Date == date);

    public async Task<IEnumerable<JournalEntry>> GetRangeAsync(DateOnly? from, DateOnly? to)
    {
        var query = WithIncludes();

        if (from is not null) query = query.Where(j => j.Date >= from);
        if (to is not null) query = query.Where(j => j.Date <= to);

        return await query.OrderBy(j => j.Date).ToListAsync();
    }

    public async Task<JournalEntry> UpsertAsync(JournalEntry entry)
    {
        // Höchstens ein Eintrag pro Tag (eindeutiger Index). IgnoreQueryFilters, weil
        // ein soft-gelöschter Tag den Index weiterhin belegt – ohne das würde ein
        // erneutes Schreiben an einem gelöschten Tag am Index scheitern statt zu wirken.
        var existing = await context.JournalEntries
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(j => j.Date == entry.Date);

        var now = DateTime.Now;

        if (existing is null)
        {
            entry.CreatedAt = now;
            entry.UpdatedAt = now;
            context.JournalEntries.Add(entry);
            await context.SaveChangesAsync();
            return await GetByDateAsync(entry.Date) ?? entry;
        }

        existing.Title = entry.Title;
        existing.Content = entry.Content;
        existing.Tags = entry.Tags;
        existing.ProjectId = entry.ProjectId;
        existing.TaskItemId = entry.TaskItemId;
        existing.UpdatedAt = now;
        // Schreiben an einem gelöschten Tag holt ihn zurück – dasselbe Verhalten wie
        // beim Körpergewicht. Alles andere wäre ein stiller Datenverlust.
        existing.DeletedAt = null;

        await context.SaveChangesAsync();
        return await GetByDateAsync(existing.Date) ?? existing;
    }

    public async Task<IEnumerable<JournalEntry>> SearchAsync(
        string? query, string? tag, DateOnly? from, DateOnly? to, int limit)
    {
        // Stufe 1 – in SQL einschränken, soweit die Spalten im Klartext liegen.
        var candidates = WithIncludes();

        if (from is not null) candidates = candidates.Where(j => j.Date >= from);
        if (to is not null) candidates = candidates.Where(j => j.Date <= to);

        // Grobfilter über LIKE; die genaue Prüfung folgt im Speicher, weil "sport" sonst
        // auch "sportverein" träfe.
        if (!string.IsNullOrWhiteSpace(tag))
        {
            var pattern = SearchPattern.For(tag.Trim().ToLowerInvariant());
            candidates = candidates.Where(j => j.Tags != null
                && EF.Functions.Like(j.Tags, pattern, SearchPattern.Escape));
        }

        // Stufe 2 – ab hier im Speicher: Erst das Materialisieren entschlüsselt Text,
        // Titel und Stimmungsnotizen.
        var loaded = await candidates.OrderByDescending(j => j.Date).ToListAsync();

        if (!string.IsNullOrWhiteSpace(tag))
        {
            var wanted = tag.Trim().ToLowerInvariant();
            loaded = loaded.Where(j => SplitTags(j.Tags).Contains(wanted)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            // Alle Begriffe müssen vorkommen (UND), Reihenfolge egal. Ein einzelner
            // langer Suchstring träfe sonst fast nie.
            var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            loaded = loaded.Where(j => Matches(j, terms)).ToList();
        }

        return loaded.Take(limit);
    }

    public async Task<IReadOnlyList<string>> GetAllTagsAsync() =>
        await context.JournalEntries
            .Where(j => j.Tags != null && j.Tags != "")
            .Select(j => j.Tags!)
            .ToListAsync();

    // Durchsucht wird der sichtbare Text, nicht das HTML: Sonst träfe eine Suche nach
    // "span" oder "style" das Markup statt des Inhalts. Die Notizsuche macht es seit
    // demselben Befund genauso (siehe NoteRepository.SearchAsync).
    private static bool Matches(JournalEntry entry, string[] terms)
    {
        var haystack = string.Join(' ',
            entry.Title ?? string.Empty,
            NoteSnippet.From(entry.Content, int.MaxValue),
            string.Join(' ', entry.Moods.Select(m => m.Note).Where(n => n is not null)));

        return terms.All(t => haystack.Contains(t, StringComparison.CurrentCultureIgnoreCase));
    }

    private static IEnumerable<string> SplitTags(string? tags) =>
        (tags ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public async Task<bool> DeleteAsync(int id)
    {
        var existing = await context.JournalEntries.FindAsync(id);
        if (existing is null || existing.DeletedAt is not null) return false;

        existing.DeletedAt = DateTime.Now;
        await context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RestoreAsync(int id)
    {
        var existing = await context.JournalEntries
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(j => j.Id == id);
        if (existing is null || existing.DeletedAt is null) return false;

        existing.DeletedAt = null;
        await context.SaveChangesAsync();
        return true;
    }

    public async Task<IEnumerable<JournalEntry>> GetDeletedAsync() =>
        await context.JournalEntries
            .IgnoreQueryFilters()
            .Where(j => j.DeletedAt != null)
            .OrderByDescending(j => j.DeletedAt)
            .ToListAsync();

    public async Task<bool> PurgeAsync(int id)
    {
        var existing = await context.JournalEntries
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(j => j.Id == id);
        if (existing is null || existing.DeletedAt is null) return false;

        // Die Stimmungen nimmt die Kaskade mit (DeleteBehavior.Cascade).
        context.JournalEntries.Remove(existing);
        await context.SaveChangesAsync();
        return true;
    }

    // ── Stimmungen ───────────────────────────────────────────────────────────────

    public async Task<MoodEntry> AddMoodAsync(DateOnly date, MoodEntry mood)
    {
        // Den Tag bei Bedarf leer anlegen: Eine Stimmung festzuhalten soll ein Klick
        // sein und nicht voraussetzen, dass man vorher etwas geschrieben hat.
        var entry = await context.JournalEntries
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(j => j.Date == date);

        if (entry is null)
        {
            var now = DateTime.Now;
            entry = new JournalEntry { Date = date, CreatedAt = now, UpdatedAt = now };
            context.JournalEntries.Add(entry);
            await context.SaveChangesAsync();
        }
        else if (entry.DeletedAt is not null)
        {
            // Analog zum Upsert: Eine neue Stimmung an einem gelöschten Tag holt ihn zurück.
            entry.DeletedAt = null;
        }

        mood.JournalEntryId = entry.Id;
        mood.CreatedAt = DateTime.Now;
        context.MoodEntries.Add(mood);

        entry.UpdatedAt = DateTime.Now;
        await context.SaveChangesAsync();
        return mood;
    }

    public async Task<MoodEntry?> UpdateMoodAsync(int id, MoodEntry updated)
    {
        var existing = await context.MoodEntries.FindAsync(id);
        if (existing is null) return null;

        existing.Mood = updated.Mood;
        existing.Energy = updated.Energy;
        existing.Note = updated.Note;
        existing.RecordedAt = updated.RecordedAt;

        await TouchDayAsync(existing.JournalEntryId);
        await context.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteMoodAsync(int id)
    {
        var existing = await context.MoodEntries.FindAsync(id);
        if (existing is null) return false;

        // Stimmungen kennen keinen Soft-Delete: Sie sind kurz und schnell neu erfasst,
        // ein zweiter Papierkorb dafür wäre mehr Verwaltung als Nutzen.
        context.MoodEntries.Remove(existing);

        await TouchDayAsync(existing.JournalEntryId);
        await context.SaveChangesAsync();
        return true;
    }

    // Hält UpdatedAt des Tages aktuell, wenn sich nur eine Stimmung ändert – sonst
    // sähe die Liste den Tag als unverändert.
    private async Task TouchDayAsync(int journalEntryId)
    {
        var day = await context.JournalEntries.FindAsync(journalEntryId);
        if (day is not null) day.UpdatedAt = DateTime.Now;
    }
}
