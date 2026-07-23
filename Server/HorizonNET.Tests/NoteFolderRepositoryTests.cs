using HorizonNET.Data.Repositories;
using HorizonNET.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HorizonNET.Tests;

// Die Regeln der manuellen Notiz-Ordner: Zyklen verhindern und beim Löschen die
// Unterordner mitnehmen, ohne die Notizen darin anzutasten.
public class NoteFolderRepositoryTests
{
    // ── Verschieben ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Move_UnterAnderenOrdner_Funktioniert()
    {
        using var db = new TestDatabase();
        var (a, b, _) = await SeedKetteAsync(db);   // a → b → c

        using (var act = db.NewContext())
            Assert.NotNull(await new NoteFolderRepository(act).MoveAsync(b, null));

        using var assert = db.NewContext();
        Assert.Null((await assert.NoteFolders.FindAsync(b))!.ParentFolderId);
        Assert.NotEqual(0, a);
    }

    // Ohne diese Prüfung hinge der Teilbaum in einem Ring und wäre von der Wurzel
    // aus nicht mehr erreichbar.
    [Fact]
    public async Task Move_UnterEigenenNachfahren_WirdAbgelehnt()
    {
        using var db = new TestDatabase();
        var (a, _, c) = await SeedKetteAsync(db);   // a → b → c

        using (var act = db.NewContext())
            Assert.Null(await new NoteFolderRepository(act).MoveAsync(a, c));

        using var assert = db.NewContext();
        Assert.Null((await assert.NoteFolders.FindAsync(a))!.ParentFolderId);   // unverändert
    }

    [Fact]
    public async Task Move_UnterSichSelbst_WirdAbgelehnt()
    {
        using var db = new TestDatabase();
        var (a, _, _) = await SeedKetteAsync(db);

        using var act = db.NewContext();
        Assert.Null(await new NoteFolderRepository(act).MoveAsync(a, a));
    }

    // ── Löschen ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_NimmtUnterordnerMit_MitGleichemZeitstempel()
    {
        using var db = new TestDatabase();
        var (a, b, c) = await SeedKetteAsync(db);

        using (var act = db.NewContext())
            Assert.True(await new NoteFolderRepository(act).DeleteAsync(a));

        using var assert = db.NewContext();
        var alle = await assert.NoteFolders.IgnoreQueryFilters()
            .Where(f => new[] { a, b, c }.Contains(f.Id)).ToListAsync();

        Assert.All(alle, f => Assert.NotNull(f.DeletedAt));
        Assert.Single(alle.Select(f => f.DeletedAt).Distinct());   // ein Vorgang
    }

    // Eine Notiz ist ein Dokument, ein Ordner nur Ablage: Das Löschen des Ordners
    // darf die Notiz nicht mitnehmen – und ihre Zuordnung bleibt stehen, damit sie
    // beim Wiederherstellen wieder darin liegt.
    [Fact]
    public async Task Delete_LaesstNotizenUnberuehrt()
    {
        using var db = new TestDatabase();
        var (a, _, _) = await SeedKetteAsync(db);
        var notizId = await SeedNotizAsync(db, a);

        using (var act = db.NewContext())
            await new NoteFolderRepository(act).DeleteAsync(a);

        using var assert = db.NewContext();
        var notiz = await assert.Notes.FindAsync(notizId);
        Assert.NotNull(notiz);                       // nicht mitgelöscht
        Assert.Null(notiz!.DeletedAt);
        Assert.Equal(a, notiz.NoteFolderId);         // Zuordnung erhalten
    }

    // ── Wiederherstellen ─────────────────────────────────────────────────────

    [Fact]
    public async Task Restore_HoltDenGanzenVorgangZurueck()
    {
        using var db = new TestDatabase();
        var (a, b, c) = await SeedKetteAsync(db);

        using (var act = db.NewContext())
            await new NoteFolderRepository(act).DeleteAsync(a);

        using (var act = db.NewContext())
            Assert.True(await new NoteFolderRepository(act).RestoreAsync(a));

        using var assert = db.NewContext();
        var aktive = await assert.NoteFolders.Where(f => new[] { a, b, c }.Contains(f.Id)).ToListAsync();
        Assert.Equal(3, aktive.Count);
    }

    // Nur den Unterordner wiederherstellen, während der Eltern-Ordner gelöscht bleibt:
    // Sonst hinge er unsichtbar unter einem gelöschten Ordner.
    [Fact]
    public async Task Restore_EinesUnterordners_HoltIhnAnDieObersteEbene()
    {
        using var db = new TestDatabase();
        var (a, b, _) = await SeedKetteAsync(db);

        using (var act = db.NewContext())
            await new NoteFolderRepository(act).DeleteAsync(a);

        using (var act = db.NewContext())
            await new NoteFolderRepository(act).RestoreAsync(b);

        using var assert = db.NewContext();
        var wieder = await assert.NoteFolders.FindAsync(b);
        Assert.NotNull(wieder);
        Assert.Null(wieder!.ParentFolderId);   // an die Wurzel geholt
    }

    // ── Papierkorb ───────────────────────────────────────────────────────────

    // Ein im selben Vorgang mitgelöschter Unterordner gehört nicht als eigener Eintrag
    // in den Papierkorb – er käme beim Wiederherstellen der Wurzel automatisch mit.
    [Fact]
    public async Task GetDeleted_ZeigtNurDieWurzelDesLoeschvorgangs()
    {
        using var db = new TestDatabase();
        var (a, _, _) = await SeedKetteAsync(db);

        using (var act = db.NewContext())
            await new NoteFolderRepository(act).DeleteAsync(a);

        using var assert = db.NewContext();
        var imPapierkorb = (await new NoteFolderRepository(assert).GetDeletedAsync()).ToList();
        Assert.Equal(a, Assert.Single(imPapierkorb).Id);
    }

    // Eigenständig gelöscht heißt eigener Eintrag – sonst ließe er sich nicht
    // wiederherstellen, solange der Eltern-Ordner aktiv bleibt.
    [Fact]
    public async Task GetDeleted_ZeigtEinzelnGeloeschtenUnterordner()
    {
        using var db = new TestDatabase();
        var (_, b, _) = await SeedKetteAsync(db);

        using (var act = db.NewContext())
            await new NoteFolderRepository(act).DeleteAsync(b);

        using var assert = db.NewContext();
        var imPapierkorb = (await new NoteFolderRepository(assert).GetDeletedAsync()).ToList();
        Assert.Equal(b, Assert.Single(imPapierkorb).Id);
    }

    [Fact]
    public async Task Purge_EntferntOrdnerUndUnterordnerEndgueltig()
    {
        using var db = new TestDatabase();
        var (a, b, c) = await SeedKetteAsync(db);

        using (var act = db.NewContext())
            await new NoteFolderRepository(act).DeleteAsync(a);
        using (var act = db.NewContext())
            Assert.True(await new NoteFolderRepository(act).PurgeAsync(a));

        using var assert = db.NewContext();
        var rest = await assert.NoteFolders.IgnoreQueryFilters()
            .Where(f => new[] { a, b, c }.Contains(f.Id)).CountAsync();
        Assert.Equal(0, rest);
    }

    // Auch beim endgültigen Löschen bleibt die Notiz – nur ihre Zuordnung fällt weg
    // (Fremdschlüssel SetNull).
    [Fact]
    public async Task Purge_LaesstNotizBestehen_UndLoestDieZuordnung()
    {
        using var db = new TestDatabase();
        var (a, _, _) = await SeedKetteAsync(db);
        var notizId = await SeedNotizAsync(db, a);

        using (var act = db.NewContext())
            await new NoteFolderRepository(act).DeleteAsync(a);
        using (var act = db.NewContext())
            await new NoteFolderRepository(act).PurgeAsync(a);

        using var assert = db.NewContext();
        var notiz = await assert.Notes.FindAsync(notizId);
        Assert.NotNull(notiz);
        Assert.Null(notiz!.NoteFolderId);
    }

    [Fact]
    public async Task Purge_EinesAktivenOrdners_WirdAbgelehnt()
    {
        using var db = new TestDatabase();
        var (a, _, _) = await SeedKetteAsync(db);

        using var act = db.NewContext();
        Assert.False(await new NoteFolderRepository(act).PurgeAsync(a));
    }

    // ── Seed-Helfer ──────────────────────────────────────────────────────────

    // Kette a → b → c (a ist Wurzel).
    private static async Task<(int A, int B, int C)> SeedKetteAsync(TestDatabase db)
    {
        using var ctx = db.NewContext();

        var a = new NoteFolder { Name = "A", CreatedAt = DateTime.Now };
        ctx.NoteFolders.Add(a);
        await ctx.SaveChangesAsync();

        var b = new NoteFolder { Name = "B", ParentFolderId = a.Id, CreatedAt = DateTime.Now };
        ctx.NoteFolders.Add(b);
        await ctx.SaveChangesAsync();

        var c = new NoteFolder { Name = "C", ParentFolderId = b.Id, CreatedAt = DateTime.Now };
        ctx.NoteFolders.Add(c);
        await ctx.SaveChangesAsync();

        return (a.Id, b.Id, c.Id);
    }

    private static async Task<int> SeedNotizAsync(TestDatabase db, int folderId)
    {
        using var ctx = db.NewContext();
        var notiz = new Note
        {
            Title = "Notiz",
            Content = string.Empty,
            NoteFolderId = folderId,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        ctx.Notes.Add(notiz);
        await ctx.SaveChangesAsync();
        return notiz.Id;
    }
}
