using HorizonNET.App.Components;
using HorizonNET.Shared.Transfer.DTOs;

namespace HorizonNET.Tests;

// Aufbereitung der Ordner für Auswahllisten: voller Pfad und – beim Verschieben –
// das Ausblenden ungültiger Ziele.
public class NoteFolderPathsTests
{
    [Fact]
    public void Pfad_EnthaeltAlleEbenen()
    {
        var alle = new[] { F(1, "Ideen"), F(2, "2026", 1), F(3, "Q1", 2) };
        var byId = alle.ToDictionary(f => f.Id);

        Assert.Equal("Ideen / 2026 / Q1", NoteFolderPaths.Path(alle[2], byId));
        Assert.Equal("Ideen", NoteFolderPaths.Path(alle[0], byId));
    }

    [Fact]
    public void Optionen_SindNachPfadSortiert()
    {
        var optionen = NoteFolderPaths.Options([F(1, "Zebra"), F(2, "Alpha")]);
        Assert.Equal(["Alpha", "Zebra"], optionen.Select(o => o.Label));
    }

    // Der Kern beim Verschieben: Ein Ordner darf weder unter sich selbst noch unter
    // einen eigenen Nachfahren – solche Ziele erst gar nicht anbieten.
    [Fact]
    public void Optionen_BlendenDenOrdnerUndSeineNachfahrenAus()
    {
        var alle = new[] { F(1, "Ideen"), F(2, "2026", 1), F(3, "Q1", 2), F(4, "Anderes") };

        var optionen = NoteFolderPaths.Options(alle, exclude: 1);

        Assert.Equal(["Anderes"], optionen.Select(o => o.Label));
    }

    [Fact]
    public void Optionen_OhneAusschluss_EnthaltenAlle()
    {
        var alle = new[] { F(1, "Ideen"), F(2, "2026", 1) };
        Assert.Equal(2, NoteFolderPaths.Options(alle).Count);
    }

    private static NoteFolderResponseDto F(int id, string name, int? parent = null) =>
        new(id, name, parent, DateTime.Now);
}
