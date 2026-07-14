using HorizonNET.Shared.Transfer.Enums;

namespace HorizonNET.Shared.Transfer.DTOs;

public record TaskTemplateCreateDto(
    string Title,
    string? Description = null,
    Priority Priority = Priority.Medium,
    int? ProjectId = null
);

public record TaskTemplateUpdateDto(
    string Title,
    string? Description,
    Priority Priority,
    int? ProjectId
);

public record TaskTemplateResponseDto(
    int Id,
    string Title,
    string? Description,
    string Priority,
    int? ProjectId,
    string? ProjectName,
    int SortOrder
);
