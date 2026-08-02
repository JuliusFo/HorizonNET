using HorizonNET.Data;
using Microsoft.AspNetCore.DataProtection;
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
    // Bewusst statisch und für alle Tests derselbe: EF cachet das Modell je Kontext-Typ,
    // und der Verschlüsselungs-Konverter hält den Protector aus dem ersten Modellaufbau
    // fest. Ein Provider je TestDatabase würde bedeuten, dass ab dem zweiten Test mit
    // einem Schlüssel entschlüsselt wird, der nicht mehr zum Modell passt.
    private static readonly IDataProtectionProvider Protection = new EphemeralDataProtectionProvider();

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public TestDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var ctx = new AppDbContext(_options, Protection);
        ctx.Database.EnsureCreated();
    }

    public AppDbContext NewContext() => new(_options, Protection);

    public void Dispose() => _connection.Dispose();
}
