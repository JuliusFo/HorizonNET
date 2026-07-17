namespace HorizonNET.Domain.Entities;

// EIN Satz einer Übung. Bewusst eine Zeile pro Satz statt "3×10 mit 12,5 kg" in einer:
// nur so lassen sich abfallende Wiederholungen (12/10/8) und Drop-Sets abbilden – und
// genau die sind oft das Fortschrittssignal.
//
// Maße kanonisch: Kilogramm, Meter, Sekunden. Die Anzeige rechnet auf km und min:sec um.
// Sonst stünden später Minuten und Sekunden gemischt in derselben Spalte.
public class ExerciseSet
{
    public int Id { get; set; }

    public int ExerciseId { get; set; }

    public Exercise? Exercise { get; set; }

    // Zeitpunkt der Ausführung; der Tag bildet die "Trainingseinheit" (eine eigene
    // Entität dafür gibt es bewusst nicht, siehe docs/konzept-sport-tracking.md).
    public DateTime PerformedAt { get; set; }

    // Position innerhalb von Übung + Tag (1, 2, 3 …); beim Anlegen max + 1.
    public int SetNumber { get; set; }

    // ── Typabhängige Felder: welche gelten, sagt Exercise.Kind ──────────────────

    // Strength, Bodyweight
    public int? Reps { get; set; }

    // Strength; bei Bodyweight das Zusatzgewicht (Weste, Scheibe) – meist null.
    // double statt decimal: EF Core warnt bei SQLite zu Recht vor decimal (landet als
    // TEXT, sortiert unzuverlässig), und für 12,5 kg ist double exakt genug.
    public double? WeightKg { get; set; }

    // Endurance
    public int? DistanceMeters { get; set; }

    public int? DurationSeconds { get; set; }

    // ── Für alle Arten ──────────────────────────────────────────────────────────

    // Gefühlte Anstrengung 1–10 (RPE). Erklärt später, warum eine Kurve flach wird.
    public int? Rpe { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }

    // Soft-Delete: null = aktiv.
    public DateTime? DeletedAt { get; set; }
}
