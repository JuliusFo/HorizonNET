using HorizonNET.Shared.Transfer.Enums;

namespace HorizonNET.Shared.Transfer.DTOs;

public record ProjectCreateDto(
    string Name,
    string? Description,
    ProjectStatus Status,
    Priority Priority
);

public record ProjectUpdateDto(
    string Name,
    string? Description,
    ProjectStatus Status,
    Priority Priority
);

public record ProjectResponseDto(
    int Id,
    string Name,
    string? Description,
    string Status,
    string Priority,
    DateTime CreatedAt,
    int TaskCount
);
