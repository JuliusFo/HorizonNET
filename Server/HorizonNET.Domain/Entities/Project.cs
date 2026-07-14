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

    public int? WorkspaceId { get; set; }

    public Workspace? Workspace { get; set; }

    // Soft-Delete: null = aktiv (siehe TaskItem.DeletedAt). Beim Löschen eines
    // Projekts werden dessen Tasks mit demselben Zeitstempel mitgestempelt.
    public DateTime? DeletedAt { get; set; }

    public ICollection<TaskItem> Tasks { get; set; } = [];
}