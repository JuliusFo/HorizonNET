namespace HorizonNET.Shared.Transfer.DTOs;

// Typ eines gelöschten Eintrags im Papierkorb. Als String über die API, damit der
// Client daraus Symbol, Bezeichnung und den passenden Wiederherstellen-/Löschen-Aufruf
// ableiten kann (analog zu SearchHitTypes).
public static class TrashItemTypes
{
    public const string Workspace = "workspace";
    public const string Project   = "project";
    public const string Task      = "task";
    public const string Note      = "note";
    public const string DailyTask = "dailytask";
}

// Ein soft-gelöschter Eintrag im Papierkorb.
// Context = ergänzende Zeile (z. B. Projekt-/Arbeitsbereichsname), rein zur Anzeige.
// DeletedAt dient der Sortierung (zuletzt gelöscht zuerst) und der Anzeige.
public record TrashItemDto(
    string Type,
    int Id,
    string Title,
    string? Context,
    DateTime DeletedAt
);
