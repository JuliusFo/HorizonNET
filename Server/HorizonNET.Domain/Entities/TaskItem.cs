using HorizonNET.Shared.Transfer.Enums;

namespace HorizonNET.Domain.Entities;

public class TaskItem
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    // Optionales Feld: externer Link zum Task. null = nicht gesetzt; die Detailseite
    // blendet das Eingabefeld nur ein, wenn ein Wert da ist oder der Nutzer es
    // hinzufügt. Erlaubt sind nur http/https, siehe TaskLink.IsValid.
    public string? Link { get; set; }

    // Optionales Feld: worauf der Task wartet (Freitext). Frisch ausgefüllt setzt es den
    // Task auf "Pausiert" – siehe ApplyWaitingForChange im TaskRepository.
    public string? WaitingFor { get; set; }

    public DateTime? DueDate { get; set; }

    public DateTime? StartTime { get; set; }

    public DateTime? EndTime { get; set; }

    public WorkStatus Status { get; set; } = WorkStatus.Planned;

    // Manuelle Reihenfolge innerhalb einer Kanban-Spalte (Status) – bei Sub-Tasks
    // die Reihenfolge innerhalb des Eltern-Tasks (SubTaskList).
    // Sortiert wird stets erst nach Status, dann nach SortOrder.
    public int SortOrder { get; set; }

    // Manuelle Reihenfolge in der Task-Liste eines Projekts; nur für Haupt-Tasks
    // belegt (Sub-Tasks nutzen dort SortOrder). Bewusst getrennt von SortOrder:
    // dieselbe Spalte kann nicht gleichzeitig die Kanban-Position tragen, weil jede
    // Status-Spalte eigene 0..n-1 vergibt und die Projektliste Status mischt.
    public int ListSortOrder { get; set; }

    public bool IsCompleted => Status == WorkStatus.Done || Status == WorkStatus.Abandoned;

    public Priority Priority { get; set; } = Priority.Medium;

    public int? ProjectId { get; set; }

    public Project? Project { get; set; }

    public int? ParentTaskId { get; set; }

    public TaskItem? ParentTask { get; set; }

    // Verknüpfung zum gespiegelten Google-Kalender-Eintrag (Einweg-Sync).
    // Wird ausschließlich serverseitig vom Sync gesetzt, nie über das Client-DTO.
    public string? GoogleEventId { get; set; }

    // Zeitstempel; ausschließlich serverseitig im Repository gesetzt.
    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    // Soft-Delete: null = aktiv. Gelöschte Zeilen werden per globalem Query-Filter
    // ausgeblendet; der Zeitstempel gruppiert einen Löschvorgang (Cascade) für Undo.
    public DateTime? DeletedAt { get; set; }

    public ICollection<TaskItem> SubTasks { get; set; } = [];

    // Erfasste Zeit-Intervalle (Start/Stop). Summe = verbrauchte Zeit.
    public ICollection<TimeEntry> TimeEntries { get; set; } = [];
}