using HorizonNET.Shared.Transfer.Enums;

namespace HorizonNET.Shared.Transfer.DTOs;

public record TaskCreateDto(
    string Title,
    string? Description,
    DateTime DueDate,
    DateTime? StartTime,
    DateTime? EndTime,
    Priority Priority,
    int ProjectId,
    int? ParentTaskId = null
);

public record TaskUpdateDto(
    string Title,
    string? Description,
    DateTime DueDate,
    DateTime? StartTime,
    DateTime? EndTime,
    bool IsCompleted,
    Priority Priority,
    int ProjectId
);

public record TaskResponseDto(
    int Id,
    string Title,
    string? Description,
    DateTime DueDate,
    DateTime? StartTime,
    DateTime? EndTime,
    bool IsCompleted,
    string Priority,
    int ProjectId,
    string ProjectName,
    int? ParentTaskId = null,
    List<TaskResponseDto>? SubTasks = null
);
