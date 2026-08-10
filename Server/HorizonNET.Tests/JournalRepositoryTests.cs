using HorizonNET.Data.Repositories;
using HorizonNET.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HorizonNET.Tests;

// Die Regeln des Journals: ein Eintrag pro Tag, mehrere Stimmungen darin, und der
// Text liegt verschlüsselt in der Datenbank. Letzteres sieht man einer Spalte nicht
// an – deshalb prüfen zwei Tests die Rohwerte am ValueConverter vorbei.
public class JournalRepositoryTests
{
    private static readonly DateOnly Tag = new(2026, 8, 2);

    // ── Ein Eintrag pro Tag ──────────────────────────────────────────────────────

    [Fact]
    public async Task Upsert_SameDateTwice_UpdatesInsteadOfDuplicating()
    {
        using var db = new TestDatabase();

        using (var act = db.NewContext())
            await new JournalRepository(act).UpsertAsync(NewEntry("Erster Text"));

        using (var act = db.NewContext())
            await new JournalRepository(act).UpsertAsync(NewEntry("Zweiter Text"));

        using var assert = db.NewContext();
        var all = await assert.JournalEntries.Where(j => j.Date == Tag).ToListAsync();
        var single = Assert.Single(all);
        Assert.Equal("Zweiter Text", single.Content);
    }

    // Der eindeutige Index gilt auch für soft-gelöschte Tage. Ohne IgnoreQueryFilters
    // im Repository würde ein erneutes Schreiben am Index scheitern statt zu wirken.
    [Fact]
    public async Task Upsert_OnDeletedDay_RevivesItInsteadOfFailing()
    {
        using var db = new TestDatabase();

        int id;
        using (var act = db.NewContext())
            id = (await new JournalRepository(act).UpsertAsync(NewEntry("Alt"))).Id;

        using (var act = db.NewContext())
            await new JournalRepository(act).DeleteAsync(id);

        using (var act = db.NewContext())
            await new JournalRepository(act).UpsertAsync(NewEntry("Neu"));

        using var assert = db.NewContext();
        var entry = await assert.JournalEntries.SingleAsync(j => j.Date == Tag);
        Assert.Null(entry.DeletedAt);
        Assert.Equal("Neu", entry.Content);
    }

    // ── Verschlüsselung ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Upsert_StoresContentAndTitleEncrypted()
    {
        using var db = new TestDatabase();

        using (var act = db.NewContext())
            await new JournalRepository(act).UpsertAsync(new JournalEntry
            {
                Date = Tag,
                Title = "Vertraulicher Titel",
                Content = "Heute war ein sehr persoenlicher Tag.",
                Tags = "test"
            });

        // Roh aus der Spalte, am Konverter vorbei.
        var (rawTitle, rawContent, rawTags) = await ReadRawEntryAsync(db);
        Assert.NotEqual("Vertraulicher Titel", rawTitle);
        Assert.NotEqual("Heute war ein sehr persoenlicher Tag.", rawContent);
        Assert.DoesNotContain("persoenlicher", rawContent);
        Assert.DoesNotContain("Vertraulicher", rawTitle);

        // Tags bleiben bewusst im Klartext – danach wird in SQL gefiltert.
        Assert.Equal("test", rawTags);

        // Ueber das Repository gelesen kommt der Klartext zurueck.
        using var assert = db.NewContext();
        var entry = await new JournalRepository(assert).GetByDateAsync(Tag);
        Assert.Equal("Vertraulicher Titel", entry!.Title);
        Assert.Equal("Heute war ein sehr persoenlicher Tag.", entry.Content);
    }

    [Fact]
    public async Task AddMood_StoresNoteEncrypted_AndKeepsNumbersReadable()
    {
        using var db = new TestDatabase();

        using (var act = db.NewContext())
            await new JournalRepository(act).AddMoodAsync(Tag, new MoodEntry
            {
                Mood = 2,
                Energy = 0,
                Note = "wenig geschlafen",
                RecordedAt = Tag.ToDateTime(new TimeOnly(7, 30))
            });

        var (rawNote, rawMood, rawEnergy) = await ReadRawMoodAsync(db);
        Assert.NotEqual("wenig geschlafen", rawNote);
        Assert.DoesNotContain("geschlafen", rawNote);
        // Zahlen bleiben lesbar: Danach wird gefiltert, sortiert und gerechnet.
        Assert.Equal(2L, rawMood);
        Assert.Equal(0L, rawEnergy);
    }

    // ── Stimmungen ───────────────────────────────────────────────────────────────

    // Eine Stimmung festzuhalten darf nicht voraussetzen, dass man vorher schreibt.
    [Fact]
    public async Task AddMood_WithoutExistingDay_CreatesEmptyDay()
    {
        using var db = new TestDatabase();

        using (var act = db.NewContext())
            await new JournalRepository(act).AddMoodAsync(Tag, new MoodEntry
            {
                Mood = 4,
                RecordedAt = Tag.ToDateTime(new TimeOnly(12, 0))
            });

        using var assert = db.NewContext();
        var entry = await new JournalRepository(assert).GetByDateAsync(Tag);
        Assert.NotNull(entry);
        Assert.Equal(string.Empty, entry!.Content);
        Assert.Single(entry.Moods);
    }

    [Fact]
    public async Task AddMood_MultiplePerDay_AreReturnedChronologically()
    {
        using var db = new TestDatabase();

        // Bewusst in falscher Reihenfolge einfuegen.
        foreach (var (hour, mood) in new[] { (20, (byte)5), (7, (byte)2), (13, (byte)3) })
        {
            using var act = db.NewContext();
            await new JournalRepository(act).AddMoodAsync(Tag, new MoodEntry
            {
                Mood = mood,
                RecordedAt = Tag.ToDateTime(new TimeOnly(hour, 0))
            });
        }

        using var assert = db.NewContext();
        var entry = await new JournalRepository(assert).GetByDateAsync(Tag);
        Assert.Equal([2, 3, 5], entry!.Moods.Select(m => (int)m.Mood));
    }

    // Endgueltiges Loeschen nimmt die Stimmungen des Tages mit (Cascade).
    [Fact]
    public async Task Purge_AlsoRemovesMoods()
    {
        using var db = new TestDatabase();

        int id;
        using (var act = db.NewContext())
        {
            var repo = new JournalRepository(act);
            await repo.AddMoodAsync(Tag, new MoodEntry { Mood = 3, RecordedAt = DateTime.Now });
            id = (await repo.GetByDateAsync(Tag))!.Id;
        }

        using (var act = db.NewContext())
            await new JournalRepository(act).DeleteAsync(id);

        using (var act = db.NewContext())
            Assert.True(await new JournalRepository(act).PurgeAsync(id));

        using var assert = db.NewContext();
        Assert.Empty(await assert.MoodEntries.IgnoreQueryFilters().ToListAsync());
    }

    // ── Hilfen ───────────────────────────────────────────────────────────────────

    private static JournalEntry NewEntry(string content) =>
        new() { Date = Tag, Content = content };

    private static async Task<(string? Title, string? Content, string? Tags)> ReadRawEntryAsync(TestDatabase db)
    {
        using var ctx = db.NewContext();
        using var cmd = ctx.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = "SELECT Title, Content, Tags FROM JournalEntries LIMIT 1";
        using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetString(0), reader.GetString(1), reader.GetString(2));
    }

    private static async Task<(string? Note, long Mood, long Energy)> ReadRawMoodAsync(TestDatabase db)
    {
        using var ctx = db.NewContext();
        using var cmd = ctx.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = "SELECT Note, Mood, Energy FROM MoodEntries LIMIT 1";
        using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2));
    }
}
