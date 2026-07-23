using HorizonNET.Shared.Transfer.DTOs;

namespace HorizonNET.App.Components;

// Baut aus der flachen Notizliste den abgeleiteten Ordnerbaum:
//   Projekte → Arbeitsbereich → Projekt → Task → Notiz
// Es sind KEINE echten Ordner, sondern eine Sicht auf die vorhandenen Zuordnungen –
// gespeichert wird dadurch nichts.
//
// Bewusst eine reine Funktion ohne Zustand und ohne Dienste: So liegt die Einsortier-Regel
// an genau einer Stelle und ist prüfbar, ohne die Seite zu bauen.
public static class NoteTreeBuilder
{
    public enum NodeKind { Group, Workspace, Project, Task, Folder }

    public sealed record Node(
        string Key,                     // stabil, für den Aufklapp-Zustand
        string Label,
        NodeKind Kind,
        List<Node> Children,
        List<NoteListItemDto> Notes);   // Notizen direkt in diesem Knoten

    // Der Baum braucht Projektname UND Arbeitsbereich; die Notiz kennt nur die ProjektId.
    public sealed record ProjectRef(int Id, string Name, string? Workspace);

    public const string OhneArbeitsbereich = "Ohne Arbeitsbereich";

    /// <param name="projects">ProjektId → Name + Arbeitsbereich (aus ProjectState/WorkspaceState).</param>
    /// <param name="taskProjects">
    /// TaskId → ProjektId. Nötig als Rückfall: Eine Notiz kann an einem Task hängen, ohne
    /// selbst ein Projekt zu tragen (Altbestand vor der Verknüpfungs-Korrektur, oder im
    /// Editor manuell so gesetzt). Ohne den Rückfall landete sie sichtbar falsch unter
    /// „Ohne Projekt", obwohl ihr Task zu einem Projekt gehört.
    /// </param>
    public static List<Node> Build(
        IEnumerable<NoteListItemDto> notes,
        IReadOnlyDictionary<int, ProjectRef> projects,
        IReadOnlyDictionary<int, int?> taskProjects)
    {
        var projekte      = new Node("root:projects",  "Projekte",        NodeKind.Group, [], []);
        var ohneProjekt   = new Node("root:noproject", "Ohne Projekt",    NodeKind.Group, [], []);
        var ohneZuordnung = new Node("root:none",      "Ohne Zuordnung",  NodeKind.Group, [], []);

        // Neueste zuerst – dieselbe Ordnung wie in der flachen Liste.
        foreach (var note in notes.OrderByDescending(n => n.UpdatedAt))
        {
            var project = ResolveProject(note, projects, taskProjects);

            if (project is not null)
            {
                var wsLabel = string.IsNullOrWhiteSpace(project.Workspace)
                    ? OhneArbeitsbereich
                    : project.Workspace!;

                var ws       = Child(projekte, $"ws:{wsLabel}", wsLabel, NodeKind.Workspace);
                var projNode = Child(ws, $"proj:{project.Id}", project.Name, NodeKind.Project);

                // Am Task hängende Notizen bekommen darunter einen eigenen Knoten,
                // Notizen direkt am Projekt stehen auf Projektebene.
                if (note.TaskItemId is int taskId)
                    Child(projNode, $"task:{taskId}", note.TaskItemTitle ?? "Task", NodeKind.Task).Notes.Add(note);
                else
                    projNode.Notes.Add(note);
            }
            else if (note.TaskItemId is int taskId)
            {
                // Task ohne Projekt (Inbox-Task).
                Child(ohneProjekt, $"task:{taskId}", note.TaskItemTitle ?? "Task", NodeKind.Task).Notes.Add(note);
            }
            else
            {
                ohneZuordnung.Notes.Add(note);
            }
        }

        var roots = new List<Node>();
        if (projekte.Children.Count > 0)      roots.Add(projekte);
        if (ohneProjekt.Children.Count > 0)   roots.Add(ohneProjekt);
        if (ohneZuordnung.Notes.Count > 0)    roots.Add(ohneZuordnung);

        foreach (var root in roots) SortRecursive(root);
        return roots;
    }

    /// <summary>
    /// Baum der MANUELL angelegten Ordner. Zweite, unabhängige Sicht neben der
    /// abgeleiteten: Eine Notiz kann in einem Ordner liegen und trotzdem an einem Projekt
    /// hängen – deshalb erscheint sie in beiden Bäumen, ohne dass sich etwas widerspricht.
    /// </summary>
    /// <remarks>
    /// Anders als beim abgeleiteten Baum bleiben LEERE Ordner sichtbar: Sie existieren als
    /// Datensatz, und ein frisch angelegter Ordner, den man nicht sieht, wäre unbrauchbar.
    ///
    /// Notizen, deren Ordner gelöscht ist (die Zuordnung bleibt für das Undo stehen),
    /// tauchen hier nicht auf – für die Oberfläche liegen sie solange „ohne Ordner".
    /// </remarks>
    public static List<Node> BuildFolders(
        IEnumerable<NoteListItemDto> notes,
        IEnumerable<NoteFolderResponseDto> folders)
    {
        var alle = folders.ToList();
        var knoten = alle.ToDictionary(
            f => f.Id,
            f => new Node($"folder:{f.Id}", f.Name, NodeKind.Folder, [], []));

        foreach (var note in notes.OrderByDescending(n => n.UpdatedAt))
            if (note.NoteFolderId is int fid && knoten.TryGetValue(fid, out var ziel))
                ziel.Notes.Add(note);

        var wurzeln = new List<Node>();
        foreach (var folder in alle)
        {
            var self = knoten[folder.Id];
            if (folder.ParentFolderId is int pid && knoten.TryGetValue(pid, out var parent))
                parent.Children.Add(self);
            else
                wurzeln.Add(self);   // auch bei verwaistem Eltern-Verweis nicht verlieren
        }

        foreach (var wurzel in wurzeln) SortRecursive(wurzel);
        wurzeln.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.CurrentCultureIgnoreCase));
        return wurzeln;
    }

    // Notizen im gesamten Teilbaum – für die Zahl an der Ordnerzeile.
    public static int CountNotes(Node node) =>
        node.Notes.Count + node.Children.Sum(CountNotes);

    // Projekt der Notiz: das eigene, sonst das ihres Tasks. Ist die Id unbekannt (Projekt
    // nicht geladen), retten wir die Notiz über den mitgelieferten Namen, statt sie
    // stillschweigend unter „Ohne Zuordnung" verschwinden zu lassen.
    private static ProjectRef? ResolveProject(
        NoteListItemDto note,
        IReadOnlyDictionary<int, ProjectRef> projects,
        IReadOnlyDictionary<int, int?> taskProjects)
    {
        var projectId = note.ProjectId
            ?? (note.TaskItemId is int taskId && taskProjects.TryGetValue(taskId, out var viaTask) ? viaTask : null);

        if (projectId is not int id) return null;
        if (projects.TryGetValue(id, out var known)) return known;

        return note.ProjectName is not null ? new ProjectRef(id, note.ProjectName, null) : null;
    }

    private static Node Child(Node parent, string key, string label, NodeKind kind)
    {
        var existing = parent.Children.FirstOrDefault(c => c.Key == key);
        if (existing is not null) return existing;

        var created = new Node(key, label, kind, [], []);
        parent.Children.Add(created);
        return created;
    }

    // Arbeitsbereiche/Projekte/Tasks alphabetisch; „Ohne Arbeitsbereich" ans Ende.
    private static void SortRecursive(Node node)
    {
        node.Children.Sort((a, b) =>
        {
            if (a.Kind == NodeKind.Workspace && b.Kind == NodeKind.Workspace)
            {
                var aLast = a.Label == OhneArbeitsbereich;
                var bLast = b.Label == OhneArbeitsbereich;
                if (aLast != bLast) return aLast ? 1 : -1;
            }
            return string.Compare(a.Label, b.Label, StringComparison.CurrentCultureIgnoreCase);
        });

        foreach (var child in node.Children) SortRecursive(child);
    }
}
