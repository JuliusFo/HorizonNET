using HorizonNET.App.Components;
using HorizonNET.Shared.Transfer.DTOs;
using HorizonNET.Shared.Transfer.Enums;

namespace HorizonNET.Tests;

// Einsortierung der Notizen in den abgeleiteten Ordnerbaum.
public class NoteTreeBuilderTests
{
    [Fact]
    public void NotizMitProjektUndTask_LandetUnterArbeitsbereichProjektTask()
    {
        var baum = NoteTreeBuilder.Build(
            [Note(1, projectId: 12, projectName: "Gepard", taskId: 500, taskTitle: "Ticket 17948")],
            Projekte(P(12, "Gepard", "Arbeit")),
            TaskProjekte());

        var projekte = Assert.Single(baum);
        Assert.Equal("Projekte", projekte.Label);
        var ws = Assert.Single(projekte.Children);
        Assert.Equal("Arbeit", ws.Label);
        var projekt = Assert.Single(ws.Children);
        Assert.Equal("Gepard", projekt.Label);
        var task = Assert.Single(projekt.Children);
        Assert.Equal("Ticket 17948", task.Label);
        Assert.Equal(1, Assert.Single(task.Notes).Id);
    }

    [Fact]
    public void NotizNurAmProjekt_StehtAufProjektebene()
    {
        var baum = NoteTreeBuilder.Build(
            [Note(2, projectId: 12, projectName: "Gepard")],
            Projekte(P(12, "Gepard", "Arbeit")),
            TaskProjekte());

        var projekt = baum[0].Children[0].Children[0];
        Assert.Empty(projekt.Children);                    // kein Task-Unterordner
        Assert.Equal(2, Assert.Single(projekt.Notes).Id);
    }

    // Der Fall aus den echten Daten: „Besprechungsnotizen" hängt an einem Task, trägt aber
    // selbst kein Projekt – der Task gehört jedoch zu einem. Ohne Rückfall landete sie
    // sichtbar falsch unter „Ohne Projekt".
    [Fact]
    public void NotizMitTaskAberOhneProjekt_ErbtDasProjektDesTasks()
    {
        var baum = NoteTreeBuilder.Build(
            [Note(3, taskId: 142, taskTitle: "Staffelleitertreffen planen")],
            Projekte(P(44, "Volleyball-KOL", "Privat")),
            TaskProjekte((142, 44)));

        var ws = Assert.Single(baum[0].Children);
        Assert.Equal("Privat", ws.Label);
        Assert.Equal("Volleyball-KOL", ws.Children[0].Label);
        Assert.Equal("Staffelleitertreffen planen", ws.Children[0].Children[0].Label);
    }

    [Fact]
    public void NotizAnTaskOhneProjekt_LandetUnterOhneProjekt()
    {
        var baum = NoteTreeBuilder.Build(
            [Note(4, taskId: 900, taskTitle: "Inbox-Task")],
            Projekte(),
            TaskProjekte((900, null)));

        var wurzel = Assert.Single(baum);
        Assert.Equal("Ohne Projekt", wurzel.Label);
        Assert.Equal("Inbox-Task", Assert.Single(wurzel.Children).Label);
    }

    [Fact]
    public void NotizOhneAllesLandetUnterOhneZuordnung()
    {
        var baum = NoteTreeBuilder.Build([Note(5)], Projekte(), TaskProjekte());

        var wurzel = Assert.Single(baum);
        Assert.Equal("Ohne Zuordnung", wurzel.Label);
        Assert.Equal(5, Assert.Single(wurzel.Notes).Id);
    }

    [Fact]
    public void ProjektOhneArbeitsbereich_KommtInEigenenKnotenAmEnde()
    {
        var baum = NoteTreeBuilder.Build(
            [
                Note(6, projectId: 30, projectName: "Allgemeines"),
                Note(7, projectId: 12, projectName: "Gepard")
            ],
            Projekte(P(30, "Allgemeines", null), P(12, "Gepard", "Arbeit")),
            TaskProjekte());

        var arbeitsbereiche = baum[0].Children.Select(c => c.Label).ToList();
        Assert.Equal(["Arbeit", NoteTreeBuilder.OhneArbeitsbereich], arbeitsbereiche);
    }

    [Fact]
    public void LeereGruppenErscheinenNicht()
    {
        // Nur eine unzugeordnete Notiz → weder „Projekte" noch „Ohne Projekt".
        var baum = NoteTreeBuilder.Build([Note(8)], Projekte(), TaskProjekte());
        Assert.Equal(["Ohne Zuordnung"], baum.Select(n => n.Label));
    }

    [Fact]
    public void MehrereNotizenAmSelbenTask_TeilenSichEinenKnoten()
    {
        var baum = NoteTreeBuilder.Build(
            [
                Note(9,  projectId: 12, projectName: "Gepard", taskId: 500, taskTitle: "Ticket"),
                Note(10, projectId: 12, projectName: "Gepard", taskId: 500, taskTitle: "Ticket")
            ],
            Projekte(P(12, "Gepard", "Arbeit")),
            TaskProjekte());

        var task = baum[0].Children[0].Children[0].Children[0];
        Assert.Equal(2, task.Notes.Count);
    }

    // ── Manuelle Ordner ──────────────────────────────────────────────────────

    [Fact]
    public void Ordnerbaum_SchachteltUnterordnerUndNotizen()
    {
        var baum = NoteTreeBuilder.BuildFolders(
            [Note(1, folderId: 2)],
            [Ordner(1, "Ideen"), Ordner(2, "2026", parent: 1)]);

        var wurzel = Assert.Single(baum);
        Assert.Equal("Ideen", wurzel.Label);
        var unter = Assert.Single(wurzel.Children);
        Assert.Equal("2026", unter.Label);
        Assert.Equal(1, Assert.Single(unter.Notes).Id);
    }

    // Anders als im abgeleiteten Baum: Ein frisch angelegter, leerer Ordner MUSS
    // sichtbar sein, sonst könnte man nichts hineinlegen.
    [Fact]
    public void LeererOrdner_BleibtSichtbar()
    {
        var baum = NoteTreeBuilder.BuildFolders([], [Ordner(1, "Leer")]);
        Assert.Equal("Leer", Assert.Single(baum).Label);
    }

    // Die Zuordnung bleibt beim Löschen des Ordners stehen (fürs Undo) – angezeigt
    // wird die Notiz solange aber nicht im Ordnerbaum.
    [Fact]
    public void NotizMitGeloeschtemOrdner_ErscheintNicht()
    {
        var baum = NoteTreeBuilder.BuildFolders([Note(1, folderId: 99)], [Ordner(1, "Ideen")]);
        Assert.Empty(Assert.Single(baum).Notes);
    }

    [Fact]
    public void NotizOhneOrdner_ErscheintNichtImOrdnerbaum()
    {
        var baum = NoteTreeBuilder.BuildFolders([Note(1)], [Ordner(1, "Ideen")]);
        Assert.Empty(Assert.Single(baum).Notes);
    }

    // ── Helfer ───────────────────────────────────────────────────────────────

    private static NoteFolderResponseDto Ordner(int id, string name, int? parent = null) =>
        new(id, name, parent, DateTime.Now);

    private static NoteListItemDto Note(
        int id, int? projectId = null, string? projectName = null,
        int? taskId = null, string? taskTitle = null, int minutenAlt = 0,
        int? folderId = null) =>
        new(id, $"Notiz {id}", null, DateTime.Now.AddMinutes(-minutenAlt),
            NoteKind.Html, null, taskId, taskTitle, projectId, projectName, folderId);

    private static NoteTreeBuilder.ProjectRef P(int id, string name, string? workspace) =>
        new(id, name, workspace);

    private static Dictionary<int, NoteTreeBuilder.ProjectRef> Projekte(
        params NoteTreeBuilder.ProjectRef[] refs) =>
        refs.ToDictionary(r => r.Id);

    private static Dictionary<int, int?> TaskProjekte(params (int TaskId, int? ProjectId)[] paare) =>
        paare.ToDictionary(p => p.TaskId, p => p.ProjectId);
}
