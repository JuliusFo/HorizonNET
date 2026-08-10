namespace HorizonNET.Domain.Entities;

// Ein Tag im Tagebuch. Anders als bei einer Notiz ist nicht der Titel die Identität,
// sondern das Datum: Es gibt höchstens einen Eintrag pro Tag (eindeutiger Index),
// ein zweiter Schreibvorgang für denselben Tag aktualisiert ihn (siehe Repository).
public class JournalEntry
{
    public int Id { get; set; }

    // Der fachliche Schlüssel. DateOnly, weil ein Tag ein Tag ist – eine Uhrzeit
    // würde hier nie befüllt (gleiche Überlegung wie bei BodyWeightEntry.MeasuredOn).
    public DateOnly Date { get; set; }

    // Optionale Überschrift für den Tag. VERSCHLÜSSELT (siehe AppDbContext).
    public string? Title { get; set; }

    // HTML aus dem RadzenHtmlEditor. VERSCHLÜSSELT (siehe AppDbContext).
    public string Content { get; set; } = string.Empty;

    // Kommaseparierte Stichworte, klein geschrieben. Bewusst im Klartext: Danach wird
    // in SQL gefiltert, und der Erkenntnisgewinn aus einem Tag ist ungleich kleiner
    // als aus dem Text selbst.
    public string? Tags { get; set; }

    // Optionale Zuordnungen; beim Löschen bleibt der Eintrag erhalten (SetNull).
    public int? ProjectId { get; set; }

    public Project? Project { get; set; }

    public int? TaskItemId { get; set; }

    public TaskItem? TaskItem { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    // Soft-Delete wie überall (null = aktiv). Journal-Einträge erscheinen aber NICHT
    // im globalen Papierkorb, sondern in einer eigenen Ansicht hinter der Sperre.
    public DateTime? DeletedAt { get; set; }

    // Stimmungen des Tages, chronologisch. Mehrere pro Tag sind der Punkt: Der Verlauf
    // ("früh mies, abends gut") trägt die Information, ein Tagesmittel mittelt sie weg.
    public ICollection<MoodEntry> Moods { get; set; } = [];
}
