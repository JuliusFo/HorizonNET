using HorizonNET.Data.Repositories;
using HorizonNET.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HorizonNET.Server.Tests;

// Der Google-Refresh-Token ist ein langlebiger Schlüssel zum Google-Konto und liegt seit
// Phase 12a verschlüsselt in der DB. Diese Tests halten zwei Dinge fest, die man einer
// Spalte von außen nicht ansieht: dass tatsächlich Chiffrat gespeichert wird, und wie
// sich der Klartext-Bestand aus der Zeit davor verhält.
public class GoogleConnectionRepositoryTests
{
    private const string Token = "1//0g-geheimer-refresh-token";

    // Ohne diesen Test würde ein versehentlich entfernter ValueConverter unbemerkt
    // bleiben – die App funktionierte weiter, nur eben wieder im Klartext.
    [Fact]
    public async Task SaveAsync_StoresTokenEncrypted()
    {
        using var db = new TestDatabase();

        using (var act = db.NewContext())
            await new GoogleConnectionRepository(act).SaveAsync(new GoogleConnection
            {
                RefreshToken = Token,
                Email = "test@example.com",
                ConnectedAtUtc = DateTime.UtcNow
            });

        // Roh aus der Spalte lesen, am Konverter vorbei.
        var raw = await ReadRawTokenAsync(db);
        Assert.NotNull(raw);
        Assert.NotEqual(Token, raw);
        Assert.DoesNotContain("geheimer", raw);

        // Über das Repository gelesen kommt derselbe Klartext wieder heraus.
        using var assert = db.NewContext();
        var conn = await new GoogleConnectionRepository(assert).GetAsync();
        Assert.Equal(Token, conn!.RefreshToken);
    }

    // Bestand aus der Zeit vor 12a lässt sich nicht entschlüsseln. Erwartet wird
    // "nicht verbunden" – und ausdrücklich keine Exception, die sonst jeden Aufruf,
    // der die Verbindung liest, mit einem 500 quittieren würde.
    [Fact]
    public async Task GetAsync_LegacyPlaintextToken_IsTreatedAsNotConnected()
    {
        using var db = new TestDatabase();

        using (var seed = db.NewContext())
            await seed.Database.ExecuteSqlAsync(
                $"INSERT INTO GoogleConnections (RefreshToken, Email, ConnectedAtUtc) VALUES ('klartext-token', 'alt@example.com', '2026-01-01 00:00:00')");

        using var assert = db.NewContext();
        Assert.Null(await new GoogleConnectionRepository(assert).GetAsync());
    }

    // Neu verbinden über den Klartext-Bestand hinweg: Der alte Wert wird ersetzt und
    // liegt danach verschlüsselt in der Spalte. Das ist der Weg zurück in einen
    // sauberen Zustand, ohne die Zeile von Hand zu löschen.
    [Fact]
    public async Task SaveAsync_OverLegacyPlaintext_ReplacesItWithCiphertext()
    {
        using var db = new TestDatabase();

        using (var seed = db.NewContext())
            await seed.Database.ExecuteSqlAsync(
                $"INSERT INTO GoogleConnections (RefreshToken, Email, ConnectedAtUtc) VALUES ('klartext-token', 'alt@example.com', '2026-01-01 00:00:00')");

        using (var act = db.NewContext())
            await new GoogleConnectionRepository(act).SaveAsync(new GoogleConnection
            {
                RefreshToken = Token,
                Email = "neu@example.com",
                ConnectedAtUtc = DateTime.UtcNow
            });

        var raw = await ReadRawTokenAsync(db);
        Assert.NotEqual("klartext-token", raw);
        Assert.NotEqual(Token, raw);

        using var assert = db.NewContext();
        var conn = await new GoogleConnectionRepository(assert).GetAsync();
        Assert.Equal(Token, conn!.RefreshToken);
        Assert.Equal("neu@example.com", conn.Email);
    }

    // Liest die Spalte über die rohe Verbindung, damit der ValueConverter nicht greift.
    // Die Verbindung hält TestDatabase offen (In-Memory-DB) – hier bewusst weder
    // geöffnet noch geschlossen, sonst wäre die Datenbank danach weg.
    private static async Task<string?> ReadRawTokenAsync(TestDatabase db)
    {
        using var ctx = db.NewContext();
        using var cmd = ctx.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = "SELECT RefreshToken FROM GoogleConnections LIMIT 1";
        return (string?)await cmd.ExecuteScalarAsync();
    }
}
