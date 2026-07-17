using HorizonNET.Shared.Transfer.Enums;

namespace HorizonNET.Domain.Entities;

// Stammdatum einer Sport-Übung. Bewusst eine eigene Entität statt Freitext im Satz:
// sonst zerfiele die Fortschrittskurve beim ersten Tippfehler in zwei Serien.
// Varianten ("Liegestütze eng") sind eigene Übungen.
public class Exercise
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    // Bestimmt die geltenden Felder am ExerciseSet und den Leitwert der Auswertung.
    public ExerciseKind Kind { get; set; } = ExerciseKind.Strength;

    // Optionaler Ausführungshinweis ("Ellenbogen eng am Körper").
    public string? Notes { get; set; }

    // Archivieren statt löschen: die Übung fällt aus der Erfassung, ihre Historie
    // und die Auswertung bleiben aber erhalten.
    public bool IsActive { get; set; } = true;

    // Manuelle Reihenfolge in der Übungsliste; beim Anlegen max + 1.
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }

    // Soft-Delete: null = aktiv. Sätze werden mit demselben Zeitstempel gestempelt,
    // damit Undo den Vorgang als Ganzes zurückholt (Muster wie Projekt → Tasks).
    public DateTime? DeletedAt { get; set; }

    public ICollection<ExerciseSet> Sets { get; set; } = [];
}
