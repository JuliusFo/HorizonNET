namespace HorizonNET.Shared.Transfer.DTOs;

public record NoteCreateDto(
    string Title,
    string? Content,
    int? TaskItemId = null,
    int? ProjectId = null
);

public record NoteUpdateDto(
    string Title,
    string? Content,
    int? TaskItemId = null,
    int? ProjectId = null
);

public record NoteResponseDto(
    int Id,
    string Title,
    string? Content,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int? TaskItemId = null,
    string? TaskItemTitle = null,
    int? ProjectId = null,
    string? ProjectName = null
);
