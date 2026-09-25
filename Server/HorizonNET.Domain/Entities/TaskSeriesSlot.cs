namespace HorizonNET.Domain.Entities;

// Ein Zeitfenster einer Terminserie: Wochentag + Uhrzeit von/bis. Eine Serie hat beliebig
// viele Slots, auch mehrere am selben Wochentag (User-Entscheid 2026-09-01). Der Generator
// legt je Slot und Kalendertag einen Termin an; der Termin merkt sich seinen Slot
// (TaskItem.SeriesSlotId), damit "gibt es den schon?" pro Slot beantwortet werden kann.
public class TaskSeriesSlot
{
    public int Id { get; set; }

    public int SeriesId { get; set; }

    public TaskSeries? Series { get; set; }

    public DayOfWeek DayOfWeek { get; set; }

    public TimeOnly StartTime { get; set; }

    public TimeOnly EndTime { get; set; }
}
