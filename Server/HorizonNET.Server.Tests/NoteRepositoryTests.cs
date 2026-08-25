using HorizonNET.Data.Repositories;
using HorizonNET.Domain.Entities;
using HorizonNET.Shared.Transfer.Enums;

namespace HorizonNET.Server.Tests;

// Die Notizsuche der globalen Palette (Strg+K). Notizen speichern HTML; gesucht werden
// soll aber, was der Nutzer SIEHT. Ein LIKE über die Content-Spalte konnte das nicht und
// lag in beide Richtungen daneben – diese Datei hält beide Richtungen fest.
public class NoteRepositoryTests
{
    private const int Limit = 5;

    // ── Falsche Treffer: Markup ist kein Inhalt ──────────────────────────────────

    [Theory]
    [InlineData("span")]
    [InlineData("style")]
    [InlineData("strong")]
    public async Task Search_DoesNotMatchMarkup(string markupWord)
    {
        using var db = new TestDatabase();
        await SeedNoteAsync(db, "Einkaufen",
            "<p><span style=\"color:red\"><strong>Milch</strong></span> und Brot</p>");

        var hits = await SearchAsync(db, markupWord);

        Assert.Empty(hits);
    }

    // Gegenprobe zum Test darüber: Dieselbe Notiz wird über ihren sichtbaren Text sehr
    // wohl gefunden – die Suche ist nicht einfach blind geworden.
    [Fact]
    public async Task Search_MatchesVisibleTextInsideMarkup()
    {
        using var db = new TestDatabase();
        await SeedNoteAsync(db, "Einkaufen",
            "<p><span style=\"color:red\"><strong>Milch</strong></span> und Brot</p>");

        var hits = await SearchAsync(db, "Milch");

        Assert.Equal("Einkaufen", Assert.Single(hits).Title);
    }

    // ── Fehlende Treffer: Tags trennen zusammenhängenden Text ────────────────────

    // Der praktisch häufigste Fall: Die Wörter stehen in zwei Absätzen. Im rohen HTML
    // steht dazwischen "</p><p>", ein Teilstring-Vergleich scheiterte daran.
    [Fact]
    public async Task Search_FindsPhraseAcrossElementBoundary()
    {
        using var db = new TestDatabase();
        await SeedNoteAsync(db, "Besprechung", "<p>Termin mit</p><p>Anna wegen Urlaub</p>");

        var hits = await SearchAsync(db, "mit Anna");

        Assert.Equal("Besprechung", Assert.Single(hits).Title);
    }

    // Seltener, aber dieselbe Ursache: Auszeichnung mitten im Wort.
    [Fact]
    public async Task Search_FindsWordSplitByFormatting()
    {
        using var db = new TestDatabase();
        await SeedNoteAsync(db, "Planung", "<p><b>Ur</b>laub im Juli</p>");

        var hits = await SearchAsync(db, "Urlaub");

        Assert.Equal("Planung", Assert.Single(hits).Title);
    }

    // HTML-Entitäten sind ebenfalls kein sichtbarer Text – NoteSnippet dekodiert sie.
    [Fact]
    public async Task Search_MatchesDecodedEntities()
    {
        using var db = new TestDatabase();
        await SeedNoteAsync(db, "Notiz", "<p>Kosten &amp; Nutzen</p>");

        Assert.Single(await SearchAsync(db, "Kosten & Nutzen"));
        Assert.Empty(await SearchAsync(db, "amp"));
    }

    // ── Titel und Zeichnungen ────────────────────────────────────────────────────

    [Fact]
    public async Task Search_MatchesTitle()
    {
        using var db = new TestDatabase();
        await SeedNoteAsync(db, "Urlaubsplanung", "<p>Nichts Besonderes</p>");

        Assert.Equal("Urlaubsplanung", Assert.Single(await SearchAsync(db, "urlaub")).Title);
    }

    // Zeichnungen werden ausschließlich über den Titel gesucht: Ihr Content ist SVG, ein
    // Treffer auf "path" oder "stroke" wäre Unsinn.
    [Fact]
    public async Task Search_Drawing_MatchesTitleButNotSvgContent()
    {
        using var db = new TestDatabase();
        await SeedNoteAsync(db, "Skizze Grundriss",
            "<svg><path stroke=\"black\" d=\"M0 0\"/></svg>", NoteKind.Drawing);

        Assert.Equal("Skizze Grundriss", Assert.Single(await SearchAsync(db, "Grundriss")).Title);
        Assert.Empty(await SearchAsync(db, "stroke"));
    }

    // ── Rahmenbedingungen ────────────────────────────────────────────────────────

    [Fact]
    public async Task Search_RespectsLimit_NewestFirst()
    {
        using var db = new TestDatabase();
        for (var i = 1; i <= Limit + 3; i++)
            await SeedNoteAsync(db, $"Urlaub {i}", "<p>Text</p>", updatedAt: DateTime.Now.AddMinutes(i));

        var hits = (await SearchAsync(db, "Urlaub")).ToList();

        Assert.Equal(Limit, hits.Count);
        Assert.Equal("Urlaub 8", hits[0].Title); // zuletzt geändert zuerst
    }

    // Soft-gelöschte Notizen sind über den globalen Query-Filter draußen – die Suche darf
    // sie nicht durch das Filtern im Speicher wieder hereinholen.
    [Fact]
    public async Task Search_IgnoresSoftDeleted()
    {
        using var db = new TestDatabase();
        var id = await SeedNoteAsync(db, "Urlaub", "<p>Text</p>");

        using (var act = db.NewContext())
            await new NoteRepository(act).DeleteAsync(id);

        Assert.Empty(await SearchAsync(db, "Urlaub"));
    }

    // Wildcards aus der Eingabe dürfen nicht als LIKE-Platzhalter wirken (SearchPattern).
    [Fact]
    public async Task Search_TreatsWildcardsAsLiteralText()
    {
        using var db = new TestDatabase();
        await SeedNoteAsync(db, "Rabatt", "<p>50% Nachlass</p>");
        await SeedNoteAsync(db, "Anderes", "<p>Nichts davon</p>");

        Assert.Equal("Rabatt", Assert.Single(await SearchAsync(db, "50%")).Title);
    }

    private static async Task<IEnumerable<Note>> SearchAsync(TestDatabase db, string query)
    {
        using var ctx = db.NewContext();
        return await new NoteRepository(ctx).SearchAsync(query, Limit);
    }

    private static async Task<int> SeedNoteAsync(
        TestDatabase db, string title, string content,
        NoteKind kind = NoteKind.Html, DateTime? updatedAt = null)
    {
        using var ctx = db.NewContext();
        var now = updatedAt ?? DateTime.Now;
        var note = new Note
        {
            Title = title, Content = content, Kind = kind, CreatedAt = now, UpdatedAt = now
        };
        ctx.Notes.Add(note);
        await ctx.SaveChangesAsync();
        return note.Id;
    }
}
