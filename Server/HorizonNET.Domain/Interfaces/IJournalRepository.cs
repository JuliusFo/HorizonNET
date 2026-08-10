using HorizonNET.Domain.Entities;

namespace HorizonNET.Domain.Interfaces;

public interface IJournalRepository
{
    // Der Eintrag eines Tages inkl. Stimmungen; null, wenn für den Tag nichts existiert.
    Task<JournalEntry?> GetByDateAsync(DateOnly date);

    // Chronologisch (ältester zuerst) – so kommen Liste, Heatmap und Kurve fertig sortiert an.
    Task<IEnumerable<JournalEntry>> GetRangeAsync(DateOnly? from, DateOnly? to);

    // Legt den Tag an oder aktualisiert ihn (höchstens einer pro Tag).
    Task<JournalEntry> UpsertAsync(JournalEntry entry);

    // Volltextsuche über Tagestext, Überschrift und Stimmungsnotizen.
    //
    // Der Weg ist zweistufig, weil die Textspalten verschlüsselt sind: Zeitraum und Tag
    // schränken per SQL ein, der Rest wird im Speicher entschlüsselt und gefiltert. Ein
    // SQL-LIKE über Chiffrat kann nicht funktionieren – gleicher Klartext ergibt jedes
    // Mal ein anderes Chiffrat, genau darum ist es sicher.
    Task<IEnumerable<JournalEntry>> SearchAsync(
        string? query, string? tag, DateOnly? from, DateOnly? to, int limit);

    // Die Tags aller Einträge, roh und unverschlüsselt – Aufteilen und Zählen macht der
    // Aufrufer. Bleibt bewusst SQL-nah: Tags sind der einzige Textteil im Klartext.
    Task<IReadOnlyList<string>> GetAllTagsAsync();

    Task<bool> DeleteAsync(int id);

    Task<bool> RestoreAsync(int id);

    // Soft-gelöschte Einträge für die eigene Papierkorb-Ansicht des Journals
    // (bewusst NICHT im globalen Papierkorb), zuletzt gelöscht zuerst.
    Task<IEnumerable<JournalEntry>> GetDeletedAsync();

    // Endgültiges Löschen (nicht umkehrbar); nimmt die Stimmungen des Tages mit.
    Task<bool> PurgeAsync(int id);

    // ── Stimmungen ───────────────────────────────────────────────────────────────

    // Hängt eine Stimmung an den Tag. Existiert der Tageseintrag noch nicht, wird er
    // leer angelegt – Stimmung festhalten darf kein Schreiben voraussetzen.
    Task<MoodEntry> AddMoodAsync(DateOnly date, MoodEntry mood);

    Task<MoodEntry?> UpdateMoodAsync(int id, MoodEntry mood);

    Task<bool> DeleteMoodAsync(int id);
}
