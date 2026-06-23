using HorizonNET.Shared.Transfer.Enums;

namespace HorizonNET.Domain.Entities;

public class Project
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ProjectStatus Status { get; set; } = ProjectStatus.Active;

    public Priority Priority { get; set; } = Priority.Medium;

    public string? Color { get; set; }

    public ICollection<TaskItem> Tasks { get; set; } = [];
}