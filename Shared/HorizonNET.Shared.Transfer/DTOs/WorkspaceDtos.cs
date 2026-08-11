namespace HorizonNET.Shared.Transfer.DTOs;

public record WorkspaceCreateDto(
    string Name,
    string? Description,
    string? Color = null
);

// Vollersatz, deshalb ohne Standardwerte (siehe NoteUpdateDto).
public record WorkspaceUpdateDto(
    string Name,
    string? Description,
    string? Color
);

public record WorkspaceResponseDto(
    int Id,
    string Name,
    string? Description,
    DateTime CreatedAt,
    int ProjectCount,
    string? Color = null
);
