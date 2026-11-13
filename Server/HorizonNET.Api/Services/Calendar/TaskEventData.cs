using HorizonNET.Domain.Entities;
using HorizonNET.Shared.Transfer;

namespace HorizonNET.Api.Services.Calendar;

// Anbieterneutrale Sicht auf den Termin, den ein Task im Kalender ergibt. Beide Provider
// bauen ihr Event daraus, damit Google und Nextcloud exakt gleich rechnen.
//
// Erledigte Tasks bleiben als Termin stehen (die Historie ist der Sinn eines Kalenders),
// werden aber entschärft: keine Erinnerung, "✓" im Titel und "frei" statt "beschäftigt".
public sealed record TaskEventData(
    string Title,
    string? Description,
    bool Busy,
    int? ReminderMinutes,
    // Bei AllDay: Start = Tag (00:00), End = Folgetag (exklusiv). Sonst lokale Zeiten.
    DateTime Start,
    DateTime End,
    bool AllDay)
{
    // Voraussetzung: task.DueDate ist gesetzt.
    public static TaskEventData From(TaskItem task, int? defaultReminderMinutes)
    {
        // Der Wert am Task schlägt den Standard; erledigt schlägt beide.
        var erinnerung = task.IsCompleted
            ? null
            : TaskReminder.Effective(task.ReminderMinutes, defaultReminderMinutes);

        var title = task.IsCompleted ? $"✓ {task.Title}" : task.Title;
        var date = task.DueDate!.Value.Date;

        if (task.StartTime is not null && task.EndTime is not null)
        {
            var start = date + task.StartTime.Value.TimeOfDay;
            var end = date + task.EndTime.Value.TimeOfDay;
            if (end <= start) end = start.AddHours(1);

            return new TaskEventData(title, task.Description, !task.IsCompleted, erinnerung,
                start, end, AllDay: false);
        }

        return new TaskEventData(title, task.Description, !task.IsCompleted, erinnerung,
            date, date.AddDays(1), AllDay: true);
    }
}
