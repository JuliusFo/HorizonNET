using HorizonNET.App.Components;
using HorizonNET.Shared.Transfer.DTOs;

namespace HorizonNET.Client.Tests;

// Aufbereitung der Tasks für die Auswahllisten im Notiz- und Zeichnungs-Editor.
// Ein Dropdown kann keine Hierarchie zeigen: Die Liste ist flach, und die Zugehörigkeit
// eines Sub-Tasks ergibt sich allein aus Position und Einrückungszeichen.
public class TaskOptionsTests
{
    [Fact]
    public void SubTasks_StehenEingeruecktDirektUnterIhremHauptTask()
    {
        var tasks = new[]
        {
            T(1, "Küche", subs: [T(2, "Fliesen"), T(3, "Armatur")]),
            T(4, "Steuer")
        };

        var optionen = TaskOptions.For(tasks);

        Assert.Equal(["Küche", "↳ Armatur", "↳ Fliesen", "Steuer"], optionen.Select(o => o.Label));
        Assert.Equal([1, 3, 2, 4], optionen.Select(o => o.Id));
    }

    // Sortiert wird kulturabhängig und ohne Rücksicht auf Groß-/Kleinschreibung – sonst
    // stünden alle klein geschriebenen Titel hinter den großen.
    [Fact]
    public void Sortierung_IgnoriertGrossKleinschreibung()
    {
        var tasks = new[] { T(1, "zebra"), T(2, "Alpha"), T(3, "beta") };

        Assert.Equal(["Alpha", "beta", "zebra"], TaskOptions.For(tasks).Select(o => o.Label));
    }

    [Fact]
    public void OhneProjektfilter_KommenAlleTasks()
    {
        var tasks = new[] { T(1, "Mit", projectId: 7), T(2, "Ohne") };

        Assert.Equal(2, TaskOptions.For(tasks).Count);
    }

    [Fact]
    public void MitProjektfilter_BleibenNurDessenTasks()
    {
        var tasks = new[] { T(1, "Küche", projectId: 7), T(2, "Steuer", projectId: 8), T(3, "Lose") };

        var optionen = TaskOptions.For(tasks, projectId: 7);

        Assert.Equal("Küche", Assert.Single(optionen).Label);
    }

    // Sub-Tasks tragen das Projekt ihres Haupt-Tasks und werden deshalb nicht eigens
    // gefiltert – sie kommen mit, wenn der Haupt-Task durchkommt.
    [Fact]
    public void MitProjektfilter_KommenDieSubTasksMit()
    {
        var tasks = new[] { T(1, "Küche", projectId: 7, subs: [T(2, "Fliesen", projectId: 7)]) };

        var optionen = TaskOptions.For(tasks, projectId: 7);

        Assert.Equal(["Küche", "↳ Fliesen"], optionen.Select(o => o.Label));
    }

    [Fact]
    public void OhneSubTasks_KommtNurDerHauptTask()
    {
        // SubTasks ist null, nicht etwa eine leere Liste – so liefert es die API.
        var optionen = TaskOptions.For([T(1, "Allein")]);

        Assert.Equal("Allein", Assert.Single(optionen).Label);
    }

    [Fact]
    public void LeereEingabe_LiefertLeereListe() => Assert.Empty(TaskOptions.For([]));

    private static TaskOptionDto T(int id, string title, int? projectId = null, List<TaskOptionDto>? subs = null) =>
        new(id, title, projectId, subs);
}
