namespace HorizonNET.Domain.Entities;

// Ein Zeit-Intervall an einem Task. Die verbrauchte Zeit eines Tasks ist die Summe
// seiner Intervalle – so lässt sich zwischendurch stoppen und später fortsetzen,
// ohne dass frühere Zeiten verloren gehen.
public class TimeEntry
{
    public int Id { get; set; }

    public int TaskItemId { get; set; }

    public TaskItem TaskItem { get; set; } = null!;

    public DateTime StartedAt { get; set; }

    // null = läuft gerade. Es darf systemweit höchstens ein laufendes Intervall
    // geben (siehe TimeEntryRepository.StartAsync).
    public DateTime? EndedAt { get; set; }

    // Soft-Delete: null = aktiv (siehe TaskItem.DeletedAt).
    public DateTime? DeletedAt { get; set; }

    public bool IsRunning => EndedAt is null;

    public TimeSpan Duration => (EndedAt ?? DateTime.Now) - StartedAt;
}
