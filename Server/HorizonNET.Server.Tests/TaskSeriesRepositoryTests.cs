using HorizonNET.Data.Repositories;
using HorizonNET.Domain.Entities;
using HorizonNET.Shared.Transfer.Enums;
using Microsoft.EntityFrameworkCore;

namespace HorizonNET.Server.Tests;

// Terminserien: Kopf + Slots. Festgehalten wird vor allem der Slot-Abgleich beim Vollersatz –
// bekannte Ids bleiben stehen (die materialisierten Termine hängen daran), neue kommen dazu,
// weggelassene fliegen raus. Gegen echtes SQLite (TestDatabase), weil Cascade und
// Soft-Delete-Filter mitspielen.
public class TaskSeriesRepositoryTests
{
    [Fact]
    public async Task Create_PersistsSlots()
    {
        using var db = new TestDatabase();

        int id;
        using (var act = db.NewContext())
        {
            var created = await new TaskSeriesRepository(act).CreateAsync(new TaskSeries
            {
                Title = "Volleyball",
                Slots =
                [
                    new() { DayOfWeek = DayOfWeek.Monday,    StartTime = new(19, 0), EndTime = new(21, 0) },
                    new() { DayOfWeek = DayOfWeek.Monday,    StartTime = new(21, 0), EndTime = new(22, 0) }, // zweiter am selben Tag
                    new() { DayOfWeek = DayOfWeek.Wednesday, StartTime = new(18, 30), EndTime = new(20, 0) },
                ]
            });
            id = created.Id;
        }

        using var assert = db.NewContext();
        var series = await assert.TaskSeries.Include(s => s.Slots).SingleAsync(s => s.Id == id);
        Assert.Equal(TaskKind.Appointment, series.Kind);   // Default: Termin
        Assert.Equal(3, series.Slots.Count);
        Assert.Equal(2, series.Slots.Count(s => s.DayOfWeek == DayOfWeek.Monday));
        Assert.NotEqual(default, series.CreatedAt);
    }

    [Fact]
    public async Task Update_SyncsSlots_KeepingKnownIds()
    {
        using var db = new TestDatabase();
        var (id, mondayId, wednesdayId) = await SeedSeriesAsync(db);

        using (var act = db.NewContext())
        {
            await new TaskSeriesRepository(act).UpdateAsync(id, new TaskSeries
            {
                Title = "Volleyball (neu)", IsActive = true, Kind = TaskKind.Appointment,
                Slots =
                [
                    // Montag bleibt (gleiche Id), bekommt neue Uhrzeit; Mittwoch fehlt → weg;
                    // Freitag ist neu.
                    new() { Id = mondayId, DayOfWeek = DayOfWeek.Monday, StartTime = new(20, 0), EndTime = new(22, 0) },
                    new() { DayOfWeek = DayOfWeek.Friday, StartTime = new(20, 0), EndTime = new(22, 0) },
                ]
            });
        }

        using var assert = db.NewContext();
        var slots = await assert.TaskSeriesSlots.Where(s => s.SeriesId == id).ToListAsync();
        Assert.Equal(2, slots.Count);

        var monday = Assert.Single(slots, s => s.DayOfWeek == DayOfWeek.Monday);
        Assert.Equal(mondayId, monday.Id);                 // Id stabil
        Assert.Equal(new TimeOnly(20, 0), monday.StartTime); // Uhrzeit übernommen

        Assert.DoesNotContain(slots, s => s.Id == wednesdayId);
        Assert.Contains(slots, s => s.DayOfWeek == DayOfWeek.Friday);
        Assert.Equal("Volleyball (neu)", (await assert.TaskSeries.FindAsync(id))!.Title);
    }

    [Fact]
    public async Task Delete_IsSoft_AndHidesSlotsWithIt()
    {
        using var db = new TestDatabase();
        var (id, _, _) = await SeedSeriesAsync(db);

        using (var act = db.NewContext())
            Assert.NotNull(await new TaskSeriesRepository(act).DeleteAsync(id));

        using var assert = db.NewContext();
        Assert.Null(await assert.TaskSeries.FirstOrDefaultAsync(s => s.Id == id));         // Filter greift
        Assert.Empty(await assert.TaskSeriesSlots.Where(s => s.SeriesId == id).ToListAsync()); // Slots mit ausgeblendet
        Assert.NotNull(await assert.TaskSeries.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == id)); // aber noch da
    }

    [Fact]
    public async Task Purge_LeavesMaterializedTasksWithoutSeriesLink()
    {
        using var db = new TestDatabase();
        var (id, mondayId, _) = await SeedSeriesAsync(db);

        int taskId;
        using (var seed = db.NewContext())
        {
            // Vergangener Termin = Historie: Der wird beim Löschen der Serie NICHT
            // mitgenommen und verliert beim Purge nur seinen Bezug.
            var task = new TaskItem
            {
                Title = "Volleyball", Kind = TaskKind.Appointment,
                SeriesId = id, SeriesSlotId = mondayId, SeriesDate = new DateOnly(2026, 1, 5)
            };
            seed.Tasks.Add(task);
            await seed.SaveChangesAsync();
            taskId = task.Id;
        }

        using (var act = db.NewContext())
        {
            var repo = new TaskSeriesRepository(act);
            await repo.DeleteAsync(id);
            Assert.True(await repo.PurgeAsync(id));
        }

        using var assert = db.NewContext();
        var orphan = (await assert.Tasks.FindAsync(taskId))!;
        Assert.Null(orphan.SeriesId);       // SetNull statt Cascade
        Assert.Null(orphan.SeriesSlotId);
        Assert.Null(await assert.TaskSeries.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == id));
    }

    // Serie, Slot und nominelles Datum sind eindeutig – auch wenn der erste Termin bereits
    // soft-gelöscht ist. Das ist die Regel, die den Generator davor bewahrt, einen
    // abgesagten Termin beim nächsten Lauf wieder anzulegen.
    [Fact]
    public async Task MaterializedTask_IsUniquePerSeriesSlotAndDate_EvenWhenSoftDeleted()
    {
        using var db = new TestDatabase();
        var (id, mondayId, _) = await SeedSeriesAsync(db);
        var date = new DateOnly(2026, 9, 28);

        using (var seed = db.NewContext())
        {
            seed.Tasks.Add(new TaskItem
            {
                Title = "abgesagt", SeriesId = id, SeriesSlotId = mondayId, SeriesDate = date,
                DeletedAt = DateTime.Now
            });
            await seed.SaveChangesAsync();
        }

        using var act = db.NewContext();
        act.Tasks.Add(new TaskItem { Title = "nochmal", SeriesId = id, SeriesSlotId = mondayId, SeriesDate = date });
        await Assert.ThrowsAsync<DbUpdateException>(() => act.SaveChangesAsync());
    }

    private static async Task<(int Id, int MondayId, int WednesdayId)> SeedSeriesAsync(TestDatabase db)
    {
        using var ctx = db.NewContext();
        var monday    = new TaskSeriesSlot { DayOfWeek = DayOfWeek.Monday,    StartTime = new(19, 0),  EndTime = new(21, 0) };
        var wednesday = new TaskSeriesSlot { DayOfWeek = DayOfWeek.Wednesday, StartTime = new(18, 30), EndTime = new(20, 0) };
        var series = new TaskSeries { Title = "Volleyball", Slots = [monday, wednesday] };
        ctx.TaskSeries.Add(series);
        await ctx.SaveChangesAsync();
        return (series.Id, monday.Id, wednesday.Id);
    }
}
