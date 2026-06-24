namespace HorizonNET.Shared.Transfer.DTOs;

public record WorkspaceCreateDto(
    string Name,
    string? Description,
    string? Color = null
);

public record WorkspaceUpdateDto(
    string Name,
    string? Description,
    string? Color = null
);

public record WorkspaceResponseDto(
    int Id,
    string Name,
    string? Description,
    DateTime CreatedAt,
    int ProjectCount,
    string? Color = null
);
