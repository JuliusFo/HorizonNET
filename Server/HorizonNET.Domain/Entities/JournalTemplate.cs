namespace HorizonNET.Domain.Entities;

// Leitfragen-Vorlage für einen Eintrag ("Abendrückblick", "Morgen-Fokus"). Muster wie
// TaskTemplate. Löst das Problem der leeren Seite, an dem Tagebücher sonst nach zwei
// Wochen sterben.
public class JournalTemplate
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    // HTML mit Überschriften/Fragen, wird beim Anwenden in den Eintrag eingefügt.
    // Bewusst NICHT verschlüsselt: Eine Vorlage enthält die Fragen, nicht die Antworten.
    public string Content { get; set; } = string.Empty;

    // Manuelle Reihenfolge in der Auswahl; beim Anlegen max + 1 (Lehre aus Phase 10b).
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }

    // Soft-Delete: null = aktiv.
    public DateTime? DeletedAt { get; set; }
}
