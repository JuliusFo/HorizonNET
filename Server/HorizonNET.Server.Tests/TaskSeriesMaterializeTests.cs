using HorizonNET.Data.Repositories;
using HorizonNET.Domain.Entities;
using HorizonNET.Shared.Transfer.Enums;
using Microsoft.EntityFrameworkCore;

namespace HorizonNET.Server.Tests;

// Der Generator-Kern (MaterializeAsync) und die Löschsemantik einer Serie. Die Regeln,
// die hier stehen, sind die, die im Alltag wehtun, wenn sie kippen: Ein abgesagter Termin
// darf nicht wiederkommen, vergangene Termine sind Historie, und ein bewusst verlegter
// Einzeltermin behält seine Zeit, wenn die Serie umgestellt wird.
public class TaskSeriesMaterializeTests
{
    // Mo 28.09.2026 … So 04.10.2026: genau eine Woche.
    private static readonly DateOnly WeekStart = new(2026, 9, 28);
    private static readonly DateOnly WeekEnd   = new(2026, 10, 4);

    [Fact]
    public async Task Materialize_CreatesOneTaskPerSlotAndMatchingDay()
    {
        using var db = new TestDatabase();
        var (id, mondayId, _) = await SeedSeriesAsync(db); // Mo 19–21, Mo 21–22, Mi 18:30–20

        IReadOnlyList<TaskItem> created;
        using (var act = db.NewContext())
            created = await new TaskSeriesRepository(act).MaterializeAsync(WeekStart, WeekEnd);

        Assert.Equal(3, created.Count);

        using var assert = db.NewContext();
        var tasks = await assert.Tasks.Where(t => t.SeriesId == id).OrderBy(t => t.StartTime).ToListAsync();
        Assert.Equal(3, tasks.Count);

        var first = tasks[0];
        Assert.Equal(mondayId, first.SeriesSlotId);
        Assert.Equal(WeekStart, first.SeriesDate);
        Assert.Equal(new DateTime(2026, 9, 28), first.DueDate);
        Assert.Equal(new DateTime(2026, 9, 28, 19, 0, 0), first.StartTime);
        Assert.Equal(new DateTime(2026, 9, 28, 21, 0, 0), first.EndTime);
        Assert.Equal(TaskKind.Appointment, first.Kind);
        Assert.Equal("Volleyball", first.Title);
    }

    [Fact]
    public async Task Materialize_IsIdempotent_AndSkipsSoftDeleted()
    {
        using var db = new TestDatabase();
        var (id, _, _) = await SeedSeriesAsync(db);

        using (var act = db.NewContext())
            await new TaskSeriesRepository(act).MaterializeAsync(WeekStart, WeekEnd);

        // Einen Termin absagen (soft-löschen) …
        using (var cancel = db.NewContext())
        {
            var one = await cancel.Tasks.FirstAsync(t => t.SeriesId == id);
            one.DeletedAt = DateTime.Now;
            await cancel.SaveChangesAsync();
        }

        // … zweiter Lauf: nichts Neues, der abgesagte kehrt nicht zurück.
        IReadOnlyList<TaskItem> second;
        using (var act = db.NewContext())
            second = await new TaskSeriesRepository(act).MaterializeAsync(WeekStart, WeekEnd);

        Assert.Empty(second);
        using var assert = db.NewContext();
        Assert.Equal(3, await assert.Tasks.IgnoreQueryFilters().CountAsync(t => t.SeriesId == id));
    }

    [Fact]
    public async Task Materialize_SkipsInactiveSeries()
    {
        using var db = new TestDatabase();
        var (id, _, _) = await SeedSeriesAsync(db, isActive: false);

        using (var act = db.NewContext())
            Assert.Empty(await new TaskSeriesRepository(act).MaterializeAsync(WeekStart, WeekEnd));

        using var assert = db.NewContext();
        Assert.Equal(0, await assert.Tasks.CountAsync(t => t.SeriesId == id));
    }

    [Fact]
    public async Task Delete_RemovesFutureTasks_KeepsPast()
    {
        using var db = new TestDatabase();
        var (id, mondayId, _) = await SeedSeriesAsync(db);
        var yesterday = DateOnly.FromDateTime(DateTime.Now).AddDays(-1);
        var nextWeek  = DateOnly.FromDateTime(DateTime.Now).AddDays(7);
        var past   = await SeedOccurrenceAsync(db, id, mondayId, yesterday);
        var future = await SeedOccurrenceAsync(db, id, mondayId, nextWeek);

        using (var act = db.NewContext())
        {
            var change = await new TaskSeriesRepository(act).DeleteAsync(id);
            Assert.NotNull(change);
            Assert.Equal([future], change.Removed.Select(t => t.Id));
        }

        using var assert = db.NewContext();
        Assert.NotNull(await assert.Tasks.FindAsync(past));                        // Historie bleibt
        Assert.Null(await assert.Tasks.FirstOrDefaultAsync(t => t.Id == future));  // Zukunft weg (Filter)
        var series = await assert.TaskSeries.IgnoreQueryFilters().SingleAsync(s => s.Id == id);
        var futureRow = await assert.Tasks.IgnoreQueryFilters().SingleAsync(t => t.Id == future);
        Assert.Equal(series.DeletedAt, futureRow.DeletedAt);                       // gleicher Stempel → Undo-Menge
    }

    [Fact]
    public async Task Restore_BringsBackOnlyTasksDeletedWithTheSeries()
    {
        using var db = new TestDatabase();
        var (id, mondayId, _) = await SeedSeriesAsync(db);
        var today = DateOnly.FromDateTime(DateTime.Now);
        var cancelled = await SeedOccurrenceAsync(db, id, mondayId, today.AddDays(7), deleted: true); // vorher einzeln abgesagt
        var future    = await SeedOccurrenceAsync(db, id, mondayId, today.AddDays(14));

        using (var act = db.NewContext())
        {
            var repo = new TaskSeriesRepository(act);
            await repo.DeleteAsync(id);
            var change = await repo.RestoreAsync(id);
            Assert.NotNull(change);
            Assert.Equal([future], change.Upserted.Select(t => t.Id));
        }

        using var assert = db.NewContext();
        Assert.NotNull(await assert.TaskSeries.FirstOrDefaultAsync(s => s.Id == id));
        Assert.NotNull(await assert.Tasks.FirstOrDefaultAsync(t => t.Id == future));
        Assert.Null(await assert.Tasks.FirstOrDefaultAsync(t => t.Id == cancelled)); // bleibt abgesagt
    }

    [Fact]
    public async Task Update_RemovedSlot_DeletesItsFutureTasks()
    {
        using var db = new TestDatabase();
        var (id, mondayId, wednesdayId) = await SeedSeriesAsync(db);
        var today = DateOnly.FromDateTime(DateTime.Now);
        var mondayTask    = await SeedOccurrenceAsync(db, id, mondayId,    today.AddDays(7));
        var wednesdayTask = await SeedOccurrenceAsync(db, id, wednesdayId, today.AddDays(9));

        using (var act = db.NewContext())
        {
            var change = await new TaskSeriesRepository(act).UpdateAsync(id, new TaskSeries
            {
                Title = "Volleyball", IsActive = true, Kind = TaskKind.Appointment,
                Slots = [new() { Id = mondayId, DayOfWeek = DayOfWeek.Monday, StartTime = new(19, 0), EndTime = new(21, 0) }]
            });
            Assert.NotNull(change);
            Assert.Equal([wednesdayTask], change.Removed.Select(t => t.Id));
        }

        using var assert = db.NewContext();
        Assert.NotNull(await assert.Tasks.FirstOrDefaultAsync(t => t.Id == mondayTask));
        Assert.Null(await assert.Tasks.FirstOrDefaultAsync(t => t.Id == wednesdayTask));
    }

    [Fact]
    public async Task Update_PropagatesHeadAndTimes_ButKeepsMovedOccurrenceTime()
    {
        using var db = new TestDatabase();
        var (id, mondayId, _) = await SeedSeriesAsync(db);
        var today = DateOnly.FromDateTime(DateTime.Now);
        var unmoved = await SeedOccurrenceAsync(db, id, mondayId, today.AddDays(7));
        var moved   = await SeedOccurrenceAsync(db, id, mondayId, today.AddDays(14), movedByDays: 1);

        using (var act = db.NewContext())
        {
            await new TaskSeriesRepository(act).UpdateAsync(id, new TaskSeries
            {
                Title = "Volleyball (Halle B)", IsActive = true, Kind = TaskKind.Appointment, Priority = Priority.High,
                Slots = [new() { Id = mondayId, DayOfWeek = DayOfWeek.Monday, StartTime = new(20, 0), EndTime = new(22, 0) }]
            });
        }

        using var assert = db.NewContext();
        var a = (await assert.Tasks.FindAsync(unmoved))!;
        Assert.Equal("Volleyball (Halle B)", a.Title);
        Assert.Equal(Priority.High, a.Priority);
        Assert.Equal(new TimeSpan(20, 0, 0), a.StartTime!.Value.TimeOfDay);  // neue Uhrzeit übernommen

        var b = (await assert.Tasks.FindAsync(moved))!;
        Assert.Equal("Volleyball (Halle B)", b.Title);                        // Kopf schon
        Assert.Equal(new TimeSpan(19, 0, 0), b.StartTime!.Value.TimeOfDay);  // Uhrzeit des verlegten bleibt
    }

    // ── Helfer ───────────────────────────────────────────────────────────────────

    private static async Task<(int Id, int MondayId, int WednesdayId)> SeedSeriesAsync(TestDatabase db, bool isActive = true)
    {
        using var ctx = db.NewContext();
        var monday    = new TaskSeriesSlot { DayOfWeek = DayOfWeek.Monday,    StartTime = new(19, 0),  EndTime = new(21, 0) };
        var monday2   = new TaskSeriesSlot { DayOfWeek = DayOfWeek.Monday,    StartTime = new(21, 0),  EndTime = new(22, 0) };
        var wednesday = new TaskSeriesSlot { DayOfWeek = DayOfWeek.Wednesday, StartTime = new(18, 30), EndTime = new(20, 0) };
        var series = new TaskSeries { Title = "Volleyball", IsActive = isActive, Slots = [monday, monday2, wednesday] };
        ctx.TaskSeries.Add(series);
        await ctx.SaveChangesAsync();
        return (series.Id, monday.Id, wednesday.Id);
    }

    // Ein materialisierter Termin (Mo 19–21) am nominellen Datum; movedByDays verlegt
    // ihn auf einen anderen Tag (DueDate ≠ SeriesDate), deleted = einzeln abgesagt.
    private static async Task<int> SeedOccurrenceAsync(
        TestDatabase db, int seriesId, int slotId, DateOnly seriesDate, int movedByDays = 0, bool deleted = false)
    {
        using var ctx = db.NewContext();
        var date = seriesDate.ToDateTime(TimeOnly.MinValue).AddDays(movedByDays);
        var task = new TaskItem
        {
            Title = "Volleyball", Kind = TaskKind.Appointment,
            DueDate = date, StartTime = date.AddHours(19), EndTime = date.AddHours(21),
            SeriesId = seriesId, SeriesSlotId = slotId, SeriesDate = seriesDate,
            DeletedAt = deleted ? DateTime.Now.AddDays(-1) : null
        };
        ctx.Tasks.Add(task);
        await ctx.SaveChangesAsync();
        return task.Id;
    }
}
