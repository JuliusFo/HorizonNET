using HorizonNET.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HorizonNET.Tests;

// Eine isolierte In-Memory-Datenbank je Test. SQLite statt des EF-InMemory-Providers,
// damit die Tests echtes relationales Verhalten prüfen: Fremdschlüssel, Cascade-Delete
// und die globalen Query-Filter (Soft-Delete) verhalten sich wie in Produktion.
//
// Die Verbindung bleibt offen: Eine ":memory:"-Datenbank existiert nur, solange ihre
// Verbindung lebt – wird sie geschlossen, ist das Schema weg. Deshalb hält diese Klasse
// die Verbindung und gibt bei Bedarf frische Kontexte darauf aus.
//
// Ein frischer Kontext je Schritt (Seed / Act / Assert) ist Absicht: So wie die App pro
// Request einen eigenen Scope bekommt, sieht auch der Test keine Änderung nur wegen des
// EF-Change-Trackings – jede Prüfung liest wirklich aus der Datenbank.
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public TestDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var ctx = new AppDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    public AppDbContext NewContext() => new(_options);

    public void Dispose() => _connection.Dispose();
}
