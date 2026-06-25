using HorizonNET.Shared.Transfer.Enums;

namespace HorizonNET.Domain.Entities;

public class TaskItem
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime? DueDate { get; set; }

    public DateTime? StartTime { get; set; }

    public DateTime? EndTime { get; set; }

    public WorkStatus Status { get; set; } = WorkStatus.Planned;

    // Manuelle Reihenfolge innerhalb einer Kanban-Spalte (Status).
    // Sortiert wird stets erst nach Status, dann nach SortOrder.
    public int SortOrder { get; set; }

    public bool IsCompleted => Status == WorkStatus.Done || Status == WorkStatus.Abandoned;

    public Priority Priority { get; set; } = Priority.Medium;

    public int? ProjectId { get; set; }

    public Project? Project { get; set; }

    public int? ParentTaskId { get; set; }

    public TaskItem? ParentTask { get; set; }

    public ICollection<TaskItem> SubTasks { get; set; } = [];
}