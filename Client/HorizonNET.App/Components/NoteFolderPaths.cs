using HorizonNET.Shared.Transfer.DTOs;

namespace HorizonNET.App.Components;

// Ordner für Auswahllisten aufbereiten. Ein Dropdown kann keine Hierarchie zeigen –
// ohne den vollen Pfad wären zwei Unterordner namens „2026" nicht auseinanderzuhalten.
// Wird von der Notizen-Seite (Ordner verschieben) und vom Editor (Notiz einsortieren)
// geteilt, damit beide dieselbe Beschriftung zeigen.
public static class NoteFolderPaths
{
    public record Option(int Id, string Label);

    private static readonly StringComparer NameComparer = StringComparer.CurrentCultureIgnoreCase;

    /// <summary>Voller Pfad eines Ordners, z. B. „Ideen / 2026".</summary>
    public static string Path(NoteFolderResponseDto folder, IReadOnlyDictionary<int, NoteFolderResponseDto> byId)
    {
        var teile = new List<string> { folder.Name };
        var current = folder;

        // Die Obergrenze sichert gegen einen Ring ab. Serverseitig ist er verhindert,
        // aber eine Endlosschleife in der Oberfläche wäre der schlechtere Ausgang.
        while (current.ParentFolderId is int pid
               && byId.TryGetValue(pid, out var parent)
               && teile.Count <= byId.Count)
        {
            teile.Insert(0, parent.Name);
            current = parent;
        }

        return string.Join(" / ", teile);
    }

    /// <summary>
    /// Alle Ordner als Auswahl, alphabetisch nach vollem Pfad.
    /// </summary>
    /// <param name="exclude">
    /// Ordner, der gerade verschoben wird: Er selbst und seine Nachfahren fallen weg.
    /// Sonst böte die Liste ein Ziel an, das der Server ablehnen muss – ein Ordner kann
    /// nicht unter sich selbst liegen.
    /// </param>
    public static List<Option> Options(IEnumerable<NoteFolderResponseDto> folders, int? exclude = null)
    {
        var alle = folders.ToList();
        var byId = alle.ToDictionary(f => f.Id);
        var gesperrt = exclude is int id ? SubtreeIds(alle, id) : [];

        return alle
            .Where(f => !gesperrt.Contains(f.Id))
            .Select(f => new Option(f.Id, Path(f, byId)))
            .OrderBy(o => o.Label, NameComparer)
            .ToList();
    }

    // Ordner-Id samt aller Nachfahren.
    private static HashSet<int> SubtreeIds(List<NoteFolderResponseDto> alle, int rootId)
    {
        var ergebnis = new HashSet<int> { rootId };
        var offen = new Queue<int>();
        offen.Enqueue(rootId);

        while (offen.Count > 0)
        {
            var current = offen.Dequeue();
            foreach (var child in alle.Where(f => f.ParentFolderId == current))
                if (ergebnis.Add(child.Id))
                    offen.Enqueue(child.Id);
        }

        return ergebnis;
    }
}
