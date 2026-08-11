namespace HorizonNET.Shared.Transfer.DTOs;

// ── Stimmungen ──────────────────────────────────────────────────────────────────

public record MoodResponseDto(
    int Id,
    DateTime RecordedAt,
    byte Mood,
    byte? Energy,
    string? Note
);

// RecordedAt optional: Ohne Angabe setzt der Server "jetzt" – der Normalfall ist
// ein Klick auf ein Emoji, ohne sich mit der Uhrzeit zu beschäftigen.
public record MoodCreateDto(
    byte Mood,
    byte? Energy = null,
    string? Note = null,
    DateTime? RecordedAt = null
);

public record MoodUpdateDto(
    byte Mood,
    byte? Energy,
    string? Note,
    DateTime RecordedAt
);

// ── Tageseintrag ────────────────────────────────────────────────────────────────

public record JournalEntryResponseDto(
    int Id,
    DateOnly Date,
    string? Title,
    string Content,
    string? Tags,
    int? ProjectId,
    string? ProjectName,
    int? TaskItemId,
    string? TaskItemTitle,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<MoodResponseDto> Moods
);

// Kein Id im Upsert-DTO: Der Schlüssel ist das Datum aus der Route, nicht die Id.
// Vollersatz statt Teil-Updates – es gibt nur einen Editor, der alle Felder zeigt
// (gleiche Begründung wie bei den Projekten und beim Sport).
// Ohne Standardwerte, weil Vollersatz (siehe NoteUpdateDto): Hier wiegt es besonders
// schwer – ein vergessenes Feld beim automatischen Speichern würde Stichworte oder
// Verknüpfung eines Tagebucheintrags stillschweigend löschen.
public record JournalEntryUpsertDto(
    string? Title,
    string Content,
    string? Tags,
    int? ProjectId,
    int? TaskItemId
);

// Schlanke Variante für Liste, Heatmap und Kurve: OHNE Content – der ist HTML und
// kann lang werden, und keine dieser Ansichten zeigt ihn. Stattdessen die Kennzahlen,
// die sie tatsächlich brauchen.
public record JournalListItemDto(
    int Id,
    DateOnly Date,
    string? Title,
    // Ob überhaupt Text da ist – der Streak zählt Tage mit Text, nicht Tage mit Stimmung.
    bool HasContent,
    string? Tags,
    int MoodCount,
    // Tief, Hoch und Mittel des Tages: Daraus baut die Mehrtages-Ansicht das Band
    // samt Durchschnittslinie. null, wenn für den Tag keine Stimmung erfasst wurde.
    byte? MoodMin,
    byte? MoodMax,
    double? MoodAvg,
    DateTime UpdatedAt
);

// Für die eigene Papierkorb-Ansicht des Journals.
public record JournalDeletedItemDto(
    int Id,
    DateOnly Date,
    string? Title,
    DateTime DeletedAt
);

// ── Tagesrückblick ──────────────────────────────────────────────────────────────

// Was die App über einen Tag ohnehin schon weiß. Bewusst READ-ONLY und nicht in den
// Eintrag kopiert: Sonst fröre man Daten ein, die sich noch ändern. Wird bei jedem
// Öffnen frisch gelesen.
public record JournalContextDto(
    IReadOnlyList<ContextTaskDto> CompletedTasks,
    int DailiesDone,
    int DailiesPlanned,
    int TrackedMinutes,
    IReadOnlyList<ContextTimeDto> TrackedPerTask,
    IReadOnlyList<ContextSportDto> Sport,
    double? BodyWeightKg
)
{
    // Ob überhaupt etwas zu zeigen ist – sonst blendet der Client den Bereich aus,
    // statt eine Zeile voller Nullen anzuzeigen.
    public bool HasAnything =>
        CompletedTasks.Count > 0 || DailiesPlanned > 0 || TrackedMinutes > 0
        || Sport.Count > 0 || BodyWeightKg is not null;
}

public record ContextTaskDto(int Id, string Title, string? ProjectName);

public record ContextTimeDto(int TaskItemId, string TaskTitle, int Minutes);

// Eine Zeile je Übung. Summary ist bereits serverseitig formuliert ("3 Sätze · 1.200 kg"
// bzw. "5,2 km in 28:30"), weil die sinnvolle Kennzahl vom Übungstyp abhängt – dieselbe
// Fallunterscheidung wie in der Sport-Auswertung.
public record ContextSportDto(string ExerciseName, string Summary);

// ── Suche ───────────────────────────────────────────────────────────────────────

// Ein Treffer. Snippet ist ein serverseitig gekürzter Klartext-Auszug – der volle
// Eintrag wird erst beim Öffnen des Tages geladen.
public record JournalSearchHitDto(
    DateOnly Date,
    string? Title,
    string Snippet,
    string? Tags,
    double? MoodAvg
);

public record JournalTagDto(string Tag, int Count);

// ── „An diesem Tag" ─────────────────────────────────────────────────────────────

// Ein zurückliegender Eintrag zum selben Kalendertag. Bewusst OHNE Text und ohne
// Stimmungsnotizen: Die Anzeige liegt auf der Heute-Seite, also außerhalb der
// Journal-Sperre. Zum Lesen führt ein Link ins Journal – und damit hinter die Sperre.
public record OnThisDayDto(
    DateOnly Date,
    bool HasContent,
    int MoodCount,
    double? MoodAvg
);

// ── Vorlagen ────────────────────────────────────────────────────────────────────

public record JournalTemplateResponseDto(
    int Id,
    string Name,
    string Content,
    int SortOrder
);

// SortOrder fehlt: Die vergibt der Server als max + 1.
public record JournalTemplateCreateDto(
    string Name,
    string Content
);

public record JournalTemplateUpdateDto(
    string Name,
    string Content,
    int SortOrder
);
