namespace HorizonNET.Domain.Entities;

// Ein Häkchen: "Daily Task X wurde am Tag Y erledigt". Unique-Index (DailyTaskId, Date)
// stellt sicher, dass es pro Tag höchstens einen Eintrag gibt.
public class DailyTaskCompletion
{
    public int Id { get; set; }

    public int DailyTaskId { get; set; }

    public DailyTask? DailyTask { get; set; }

    public DateOnly Date { get; set; }
}
