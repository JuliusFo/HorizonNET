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

    // Verknüpftes Azure-DevOps-Team-Projekt (Name). Der Import auf der Projektseite zeigt
    // nur Work Items aus genau diesem Projekt. Gesetzt ausschließlich über das Teil-Update
    // SetAzureDevOpsProjectAsync – der Vollersatz UpdateAsync lässt es in Ruhe.
    public string? AzureDevOpsProject { get; set; }

    // Soft-Delete: null = aktiv (siehe TaskItem.DeletedAt). Beim Löschen eines
    // Projekts werden dessen Tasks mit demselben Zeitstempel mitgestempelt.
    public DateTime? DeletedAt { get; set; }

    public ICollection<TaskItem> Tasks { get; set; } = [];
}