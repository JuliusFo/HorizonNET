namespace HorizonNET.Domain.Entities;

// Körpergewicht des Nutzers. Zwei Gründe für eine eigene Tabelle: Sie ist die einzige
// Zahl, die Körpergewichts-Übungen vergleichbar macht (30 Liegestütze bei 80 kg sind
// mehr Arbeit als bei 75 kg), und sie ergibt für sich genommen eine Fortschrittskurve.
public class BodyWeightEntry
{
    public int Id { get; set; }

    // Tag der Messung; höchstens ein Eintrag pro Tag (eindeutiger Index).
    public DateOnly MeasuredOn { get; set; }

    public double WeightKg { get; set; }

    public DateTime CreatedAt { get; set; }

    // Soft-Delete: null = aktiv.
    public DateTime? DeletedAt { get; set; }
}
