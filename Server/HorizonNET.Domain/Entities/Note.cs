namespace HorizonNET.Domain.Entities;

public class Note
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    // HTML-Inhalt (aus dem RadzenHtmlEditor).
    public string Content { get; set; } = string.Empty;

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
