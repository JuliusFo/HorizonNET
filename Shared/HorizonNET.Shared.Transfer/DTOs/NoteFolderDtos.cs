namespace HorizonNET.Shared.Transfer.DTOs;

// Manuelle Notiz-Ordner. Flach übertragen – den Baum baut der Client, der sie ohnehin
// neben der abgeleiteten Sicht (Projekt/Task) darstellt.
public record NoteFolderResponseDto(
    int Id,
    string Name,
    int? ParentFolderId,
    DateTime CreatedAt
);

public record NoteFolderCreateDto(
    string Name,
    int? ParentFolderId = null
);

// Umbenennen und Verschieben sind getrennte Endpunkte: Am Verschieben hängt die
// Zyklusprüfung, die beim Umbenennen nur im Weg stünde.
public record NoteFolderRenameDto(string Name);

public record NoteFolderMoveDto(int? ParentFolderId);
