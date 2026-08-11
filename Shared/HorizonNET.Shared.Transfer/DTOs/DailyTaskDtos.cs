namespace HorizonNET.Shared.Transfer.DTOs;

public record DailyTaskCreateDto(
    string Title,
    int? ProjectId = null,
    byte WeekdayMask = 127
);

// Vollersatz, deshalb ohne Standardwerte (siehe NoteUpdateDto).
// Achtung bei WeekdayMask: Vorher fiel ein fehlendes Feld auf 127 ("täglich"), jetzt auf 0
// ("nie"). Das betrifft nur Aufrufer, die das Feld im JSON weglassen – die App schickt es
// überall mit. 0 ist ein gültiger Wert (kein Wochentag angehakt) und wird deshalb bewusst
// nicht serverseitig abgefangen.
public record DailyTaskUpdateDto(
    string Title,
    bool IsActive,
    int? ProjectId,
    byte WeekdayMask
);

public record DailyTaskResponseDto(
    int Id,
    string Title,
    int SortOrder,
    bool IsActive,
    int? ProjectId,
    string? ProjectName,
    // Wochentags-Muster (Bitmaske, Bit-Index = (int)DayOfWeek, 127 = täglich).
    byte WeekdayMask,
    // Für die Heute-Ansicht berechnet:
    bool CompletedToday,
    int CurrentStreak
);
