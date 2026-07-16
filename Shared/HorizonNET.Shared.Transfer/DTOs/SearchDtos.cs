namespace HorizonNET.Shared.Transfer.DTOs;

// Typ eines Suchtreffers. Als String über die API, damit der Client daraus
// direkt Route und Symbol ableiten kann.
public static class SearchHitTypes
{
    public const string Task    = "task";
    public const string Project = "project";
    public const string Note    = "note";

    // Zeichnungen sind technisch Notizen (Kind == Drawing) und springen ebenfalls auf
    // /notes/{id} – eigener Typ nur, damit die Palette sie mit eigenem Symbol zeigt.
    public const string Drawing = "drawing";
}

// Ein Treffer der globalen Suche.
// Context = ergänzende Zeile (z. B. Projektname beim Task), rein zur Anzeige.
public record SearchHitDto(
    string Type,
    int Id,
    string Title,
    string? Context = null
);
