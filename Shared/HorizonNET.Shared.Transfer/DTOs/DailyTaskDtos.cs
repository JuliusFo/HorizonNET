namespace HorizonNET.Shared.Transfer.DTOs;

public record DailyTaskCreateDto(
    string Title,
    int? ProjectId = null,
    byte WeekdayMask = 127
);

public record DailyTaskUpdateDto(
    string Title,
    bool IsActive,
    int? ProjectId = null,
    byte WeekdayMask = 127
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
