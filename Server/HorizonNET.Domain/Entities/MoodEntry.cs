namespace HorizonNET.Domain.Entities;

// Ein Stimmungs-Schlaglicht innerhalb eines Tages. Mehrere pro Tag sind ausdrücklich
// vorgesehen – erst der Verlauf macht die Kurve lesbar.
public class MoodEntry
{
    public int Id { get; set; }

    // Anker ist der Tag, nicht der Text: Wer nur schnell eine Stimmung festhält, soll
    // das können, ohne vorher etwas zu schreiben. Das Repository legt den Tageseintrag
    // dafür bei Bedarf leer an.
    public int JournalEntryId { get; set; }

    public JournalEntry? JournalEntry { get; set; }

    // Freier Zeitstempel (Default: jetzt), nachträglich änderbar – damit man abends
    // noch den Vormittag nachtragen kann. Sortierschlüssel innerhalb des Tages.
    public DateTime RecordedAt { get; set; }

    // 1..5, Emoji-Auswahl. Pflicht: Ohne Stimmung gibt es keinen Grund für die Zeile.
    public byte Mood { get; set; }

    // 0..10. null = nicht erfasst; 0 ist ein GÜLTIGER Wert ("komplett leer") und
    // bedeutet ausdrücklich nicht dasselbe wie null.
    public byte? Energy { get; set; }

    // Das "weil" – "wenig geschlafen", "Powernap und Volleyball".
    // VERSCHLÜSSELT wie der Tagestext (siehe AppDbContext).
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }
}
