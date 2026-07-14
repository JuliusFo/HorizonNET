namespace HorizonNET.Domain.Entities;

// Vorlage für eine wiederkehrende tägliche Aufgabe ("Gewohnheit"). Es wird KEIN
// Task pro Tag erzeugt – erledigt wird über DailyTaskCompletion-Einträge (Variante A).
public class DailyTask
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    // Manuelle Reihenfolge in der Heute-Liste.
    public int SortOrder { get; set; }

    // Pausieren statt löschen: inaktive Dailies erscheinen nicht in der Heute-Liste.
    public bool IsActive { get; set; } = true;

    // Wochentags-Muster als Bitmaske: Bit-Index = (int)DayOfWeek (So=0 … Sa=6).
    // 127 = alle sieben Tage (täglich). "Heute geplant?" = (Mask & (1 << (int)date.DayOfWeek)) != 0.
    public byte WeekdayMask { get; set; } = 127;

    // Optionale Zuordnung zu einem Projekt (SetNull beim Löschen des Projekts).
    public int? ProjectId { get; set; }

    public Project? Project { get; set; }

    // Soft-Delete: null = aktiv (siehe TaskItem.DeletedAt). Nicht zu verwechseln
    // mit IsActive (Pausieren) – gelöschte Dailies sind komplett ausgeblendet.
    public DateTime? DeletedAt { get; set; }

    public ICollection<DailyTaskCompletion> Completions { get; set; } = [];
}
