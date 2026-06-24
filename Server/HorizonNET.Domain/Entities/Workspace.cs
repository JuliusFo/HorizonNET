namespace HorizonNET.Domain.Entities;

public class Workspace
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string? Color { get; set; }

    public ICollection<Project> Projects { get; set; } = [];
}
