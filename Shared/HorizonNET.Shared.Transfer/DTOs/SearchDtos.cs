namespace HorizonNET.Shared.Transfer.DTOs;

// Typ eines Suchtreffers. Als String über die API, damit der Client daraus
// direkt Route und Symbol ableiten kann.
public static class SearchHitTypes
{
    public const string Task    = "task";
    public const string Project = "project";
    public const string Note    = "note";
}

// Ein Treffer der globalen Suche.
// Context = ergänzende Zeile (z. B. Projektname beim Task), rein zur Anzeige.
public record SearchHitDto(
    string Type,
    int Id,
    string Title,
    string? Context = null
);
