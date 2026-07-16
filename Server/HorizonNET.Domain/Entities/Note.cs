using HorizonNET.Shared.Transfer.Enums;

namespace HorizonNET.Domain.Entities;

public class Note
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    // Bei Kind == Html der HTML-Inhalt (aus dem RadzenHtmlEditor),
    // bei Kind == Drawing das SVG der Zeichnung.
    public string Content { get; set; } = string.Empty;

    // Art der Notiz: normale HTML-Notiz oder Zeichnung. Wird beim Anlegen gesetzt und
    // danach nicht mehr geändert (kein Umwandeln Notiz ↔ Zeichnung).
    public NoteKind Kind { get; set; } = NoteKind.Html;

    // Nur bei Kind == Drawing: kleines PNG (data:-URI) für die schlanke Listenvorschau,
    // damit die Liste nicht das komplette SVG laden muss.
    public string? Thumbnail { get; set; }

    // Zeitstempel; ausschließlich serverseitig im Repository gesetzt.
    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    // Optionale Zuordnung zu einem Task. Beim Löschen des Tasks bleibt die Notiz
    // erhalten (FK wird auf null gesetzt – SetNull).
    public int? TaskItemId { get; set; }

    public TaskItem? TaskItem { get; set; }

    // Optionale Zuordnung zu einem Projekt (analog, SetNull beim Löschen).
    public int? ProjectId { get; set; }

    public Project? Project { get; set; }

    // Soft-Delete: null = aktiv (siehe TaskItem.DeletedAt).
    public DateTime? DeletedAt { get; set; }
}
