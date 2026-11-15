using HorizonNET.Shared.Transfer.Enums;

namespace HorizonNET.Shared.Transfer.DTOs;

// Ein Task in der Wochenabschluss-Mail. ParentTitle ist bei erledigten Sub-Tasks gesetzt
// ("Eltern › Sub"), ProjectName ist bei Sub-Tasks das Projekt des Eltern-Tasks.
public record WeeklyReportTaskDto(
    int Id,
    string Title,
    string? ParentTitle,
    string? ProjectName,
    WorkStatus Status,
    Priority Priority,
    string? WaitingFor,
    DateTime? DueDate,
    DateTime? CompletedAt
);

// Grundlage der Wochenabschluss-Mail: was in der Woche ab WeekStart (Montag) erledigt
// wurde und was jetzt noch offen ist.
public record WeeklyReportDto(
    DateOnly WeekStart,
    List<WeeklyReportTaskDto> Completed,
    List<WeeklyReportTaskDto> Open
);
