using HorizonNET.Shared.Transfer.Enums;

namespace HorizonNET.Shared.Transfer.DTOs;

public record NoteCreateDto(
    string Title,
    string? Content,
    int? TaskItemId = null,
    int? ProjectId = null,
    // Kind wird nur beim Anlegen gesetzt (danach unveränderlich). Thumbnail nur bei Zeichnungen.
    NoteKind Kind = NoteKind.Html,
    string? Thumbnail = null
);

public record NoteUpdateDto(
    string Title,
    string? Content,
    int? TaskItemId = null,
    int? ProjectId = null,
    // Kind bewusst NICHT im Update-DTO – eine Notiz wird nicht in eine Zeichnung umgewandelt.
    string? Thumbnail = null
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
    string? ProjectName = null,
    NoteKind Kind = NoteKind.Html,
    string? Thumbnail = null
);

// Schlanke Variante für Listen (9e): OHNE Content – bei Zeichnungen kann das SVG
// mehrere hundert KB groß sein, das würde die Liste sonst komplett mitladen. Stattdessen
// ein serverseitig gekürzter Klartext-Snippet (nur bei HTML) bzw. das Thumbnail (Zeichnung).
public record NoteListItemDto(
    int Id,
    string Title,
    string? Snippet,
    DateTime UpdatedAt,
    NoteKind Kind,
    string? Thumbnail,
    int? TaskItemId = null,
    string? TaskItemTitle = null,
    int? ProjectId = null,
    string? ProjectName = null
)
{
    // Baut das Listen-Item aus einer vollen Notiz – genutzt vom Client, um die Liste
    // nach Anlegen/Speichern in-place zu aktualisieren, ohne die Liste neu zu laden.
    public static NoteListItemDto From(NoteResponseDto n) =>
        new(n.Id, n.Title,
            n.Kind == NoteKind.Html ? NoteSnippet.From(n.Content) : null,
            n.UpdatedAt, n.Kind, n.Thumbnail,
            n.TaskItemId, n.TaskItemTitle, n.ProjectId, n.ProjectName);
}
