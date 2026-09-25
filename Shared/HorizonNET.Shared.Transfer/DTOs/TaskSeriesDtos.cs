using HorizonNET.Shared.Transfer.Enums;

namespace HorizonNET.Shared.Transfer.DTOs;

// Ein Zeitfenster der Serie. Id null/0 = neu; beim Vollersatz bleiben Slots mit bekannter
// Id erhalten (der Server gleicht ab), damit materialisierte Termine ihren Slot behalten.
public record TaskSeriesSlotDto(
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int? Id = null
);

public record TaskSeriesCreateDto(
    string Title,
    string? Description,
    int? ProjectId,
    List<TaskSeriesSlotDto> Slots,
    TaskKind Kind = TaskKind.Appointment,
    Priority Priority = Priority.Medium,
    // Erinnerung, die jeder Termin der Serie erbt; null = Standard. Siehe TaskReminder.
    int? ReminderMinutes = null
);

// Vollersatz, deshalb ohne Standardwerte (siehe DailyTaskUpdateDto). Die Slot-Liste
// ersetzt den Bestand: fehlende Slots werden entfernt.
public record TaskSeriesUpdateDto(
    string Title,
    string? Description,
    int? ProjectId,
    bool IsActive,
    TaskKind Kind,
    Priority Priority,
    int? ReminderMinutes,
    List<TaskSeriesSlotDto> Slots
);

public record TaskSeriesResponseDto(
    int Id,
    string Title,
    string? Description,
    int? ProjectId,
    string? ProjectName,
    bool IsActive,
    TaskKind Kind,
    string Priority,
    int? ReminderMinutes,
    List<TaskSeriesSlotDto> Slots,
    DateTime CreatedAt,
    DateTime UpdatedAt
);
