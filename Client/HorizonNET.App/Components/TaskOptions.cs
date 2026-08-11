using HorizonNET.Shared.Transfer.DTOs;

namespace HorizonNET.App.Components;

// Tasks für Auswahllisten aufbereiten. Gegenstück zu NoteFolderPaths: Ein Dropdown kann
// keine Hierarchie zeigen, deshalb stehen Sub-Tasks direkt unter ihrem Haupt-Task und
// tragen ein vorangestelltes Zeichen als Einrückung.
//
// Wird vom Notiz- und vom Zeichnungs-Editor geteilt – die Aufbereitung stand vorher
// zeichengleich in beiden.
public static class TaskOptions
{
    // Markiert einen Sub-Task in der flachen Liste. Ein Einzug aus Leerzeichen ginge in
    // der Dropdown-Darstellung unter.
    private const string SubTaskPrefix = "↳ ";

    private static readonly StringComparer NameComparer = StringComparer.CurrentCultureIgnoreCase;

    /// <summary>
    /// Alle Tasks als Auswahl, alphabetisch nach Titel; Sub-Tasks eingerückt hinter
    /// ihrem Haupt-Task.
    /// </summary>
    /// <param name="projectId">
    /// Ist ein Projekt gewählt, nur dessen Tasks anbieten – sonst alle. Die Sub-Tasks
    /// erben das Projekt ihres Haupt-Tasks und werden deshalb nicht eigens gefiltert.
    /// </param>
    public static List<SelectOption> For(IEnumerable<TaskOptionDto> tasks, int? projectId = null)
    {
        var quelle = projectId is int id ? tasks.Where(t => t.ProjectId == id) : tasks;

        var optionen = new List<SelectOption>();
        foreach (var task in quelle.OrderBy(t => t.Title, NameComparer))
        {
            optionen.Add(new SelectOption(task.Id, task.Title));

            foreach (var sub in (task.SubTasks ?? []).OrderBy(s => s.Title, NameComparer))
                optionen.Add(new SelectOption(sub.Id, SubTaskPrefix + sub.Title));
        }

        return optionen;
    }
}
