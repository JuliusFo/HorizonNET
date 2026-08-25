using HorizonNET.Data.Repositories;

namespace HorizonNET.Server.Tests;

// Erste serverseitige Einstellung überhaupt: Alles andere liegt im localStorage des
// Clients. Diese Tests halten das Verhalten fest, auf das sich der Google-Sync verlässt –
// vor allem, dass ein NICHT gesetzter Wert als "nicht gesetzt" zurückkommt und nicht als 0.
public class AppSettingRepositoryTests
{
    private const string Key = "google.reminderMinutes";

    [Fact]
    public async Task GetAsync_OhneWert_LiefertNull()
    {
        using var db = new TestDatabase();
        using var ctx = db.NewContext();

        Assert.Null(await new AppSettingRepository(ctx).GetAsync(Key));
    }

    [Fact]
    public async Task SetAsync_LegtAnUndUeberschreibt()
    {
        using var db = new TestDatabase();

        using (var act = db.NewContext())
            await new AppSettingRepository(act).SetAsync(Key, "15");

        using (var act = db.NewContext())
            Assert.Equal("15", await new AppSettingRepository(act).GetAsync(Key));

        // Zweiter Aufruf darf keine zweite Zeile anlegen, sondern muss ersetzen –
        // der Schlüssel ist der Primärschlüssel, ein Insert würde hier werfen.
        using (var act = db.NewContext())
            await new AppSettingRepository(act).SetAsync(Key, "30");

        using (var act = db.NewContext())
        {
            Assert.Equal("30", await new AppSettingRepository(act).GetAsync(Key));
            Assert.Single(act.AppSettings);
        }
    }

    // "Keine Erinnerung" wird als leere Zeichenkette abgelegt. Der Sync liest das über
    // int.TryParse, das dabei false liefert – also keine Erinnerung statt 0 Minuten
    // ("zum Termin"), was ein hörbarer Unterschied wäre.
    [Fact]
    public async Task LeererWert_IstKeineNullMinuten()
    {
        using var db = new TestDatabase();

        using (var act = db.NewContext())
            await new AppSettingRepository(act).SetAsync(Key, string.Empty);

        using var ctx = db.NewContext();
        var gespeichert = await new AppSettingRepository(ctx).GetAsync(Key);

        Assert.False(int.TryParse(gespeichert, out _));
    }
}
