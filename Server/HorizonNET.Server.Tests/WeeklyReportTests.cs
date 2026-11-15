using HorizonNET.Api.Controllers;
using HorizonNET.Data.Repositories;
using HorizonNET.Domain.Entities;
using HorizonNET.Shared.Transfer.Enums;

namespace HorizonNET.Server.Tests;

// Auswahl für die Wochenabschluss-Mail: was zählt als "diese Woche erledigt", was als
// "steht noch an", und wie greift der Arbeitsbereich-Filter.
public class WeeklyReportTests
{
    private static readonly DateOnly Monday = new(2026, 11, 9);
    private static readonly DateTime InWeek = new(2026, 11, 12, 10, 0, 0);

    [Fact]
    public async Task Completed_OnlyDoneTasksInsideTheWeek()
    {
        using var db = new TestDatabase();
        using (var ctx = db.NewContext())
        {
            ctx.Tasks.AddRange(
                Task("Drin", WorkStatus.Done, InWeek),
                Task("Sonntag spät", WorkStatus.Done, new DateTime(2026, 11, 15, 23, 59, 0)),
                Task("Vorwoche", WorkStatus.Done, new DateTime(2026, 11, 8, 23, 59, 0)),
                Task("Nächste Woche", WorkStatus.Done, new DateTime(2026, 11, 16, 0, 0, 0)),
                Task("Verworfen", WorkStatus.Abandoned, InWeek),
                Task("Termin", WorkStatus.Done, InWeek, TaskKind.Appointment));
            await ctx.SaveChangesAsync();
        }

        using var act = db.NewContext();
        var report = await new TaskRepository(act).GetWeeklyReportAsync(Monday, [], false);

        Assert.Equal(["Drin", "Sonntag spät"], report.Completed.Select(t => t.Title));
    }

    [Fact]
    public async Task Completed_SubTaskOnlyWhenParentNotDoneSameWeek()
    {
        using var db = new TestDatabase();
        using (var ctx = db.NewContext())
        {
            var openParent = Task("Offen", WorkStatus.InProgress);
            openParent.SubTasks.Add(Task("Sub von offen", WorkStatus.Done, InWeek));
            var doneParent = Task("Fertig", WorkStatus.Done, InWeek);
            doneParent.SubTasks.Add(Task("Sub von fertig", WorkStatus.Done, InWeek));
            ctx.Tasks.AddRange(openParent, doneParent);
            await ctx.SaveChangesAsync();
        }

        using var act = db.NewContext();
        var report = await new TaskRepository(act).GetWeeklyReportAsync(Monday, [], false);

        Assert.Equal(["Sub von offen", "Fertig"], report.Completed.Select(t => t.Title).Order().Reverse());
        Assert.Equal("Offen", report.Completed.Single(t => t.Title == "Sub von offen").ParentTask!.Title);
    }

    [Fact]
    public async Task Open_TopLevelUnfinishedTasks_WithoutArchivedProjectsAndAppointments()
    {
        using var db = new TestDatabase();
        using (var ctx = db.NewContext())
        {
            var archived = new Project { Name = "Alt", Status = ProjectStatus.Archived };
            var parent = Task("Eltern", WorkStatus.Planned);
            parent.SubTasks.Add(Task("Sub", WorkStatus.Planned));
            ctx.Tasks.AddRange(
                parent,
                Task("Läuft", WorkStatus.InProgress),
                Task("Wartet", WorkStatus.Paused),
                Task("Erledigt", WorkStatus.Done, InWeek),
                Task("Termin", WorkStatus.Planned, kind: TaskKind.Appointment),
                Task("Archiviert", WorkStatus.Planned, project: archived));
            await ctx.SaveChangesAsync();
        }

        using var act = db.NewContext();
        var report = await new TaskRepository(act).GetWeeklyReportAsync(Monday, [], false);

        Assert.Equal(["Eltern", "Läuft", "Wartet"], report.Open.Select(t => t.Title).Order());
    }

    [Fact]
    public async Task WorkspaceFilter_UsesParentProjectForSubTasks_AndOptionalUnassigned()
    {
        using var db = new TestDatabase();
        int workId;
        using (var ctx = db.NewContext())
        {
            var work = new Workspace { Name = "Arbeit" };
            var home = new Workspace { Name = "Privat" };
            var workProject = new Project { Name = "Job", Workspace = work };
            var homeProject = new Project { Name = "Haus", Workspace = home };
            var loose = new Project { Name = "Lose" };

            var parent = Task("Job-Eltern", WorkStatus.InProgress, project: workProject);
            parent.SubTasks.Add(Task("Job-Sub", WorkStatus.Done, InWeek));
            ctx.Tasks.AddRange(
                parent,
                Task("Haus-Task", WorkStatus.Done, InWeek, project: homeProject),
                Task("Lose-Task", WorkStatus.Done, InWeek, project: loose),
                Task("Inbox-Task", WorkStatus.Planned));
            await ctx.SaveChangesAsync();
            workId = work.Id;
        }

        using (var act = db.NewContext())
        {
            var onlyWork = await new TaskRepository(act).GetWeeklyReportAsync(Monday, [workId], false);
            Assert.Equal(["Job-Sub"], onlyWork.Completed.Select(t => t.Title));
            Assert.Equal(["Job-Eltern"], onlyWork.Open.Select(t => t.Title));
        }

        using (var act = db.NewContext())
        {
            var withUnassigned = await new TaskRepository(act).GetWeeklyReportAsync(Monday, [workId], true);
            Assert.Equal(["Job-Sub", "Lose-Task"], withUnassigned.Completed.Select(t => t.Title).Order());
            Assert.Equal(["Inbox-Task", "Job-Eltern"], withUnassigned.Open.Select(t => t.Title).Order());
        }
    }

    // Ein Projekt aus einem gelöschten Arbeitsbereich zeigt die App ungruppiert – im
    // Bericht zählt es deshalb als "ohne Arbeitsbereich", statt ohne Hinweis zu fehlen.
    [Fact]
    public async Task WorkspaceFilter_ProjectOfDeletedWorkspace_CountsAsUnassigned()
    {
        using var db = new TestDatabase();
        int workId;
        using (var ctx = db.NewContext())
        {
            var work = new Workspace { Name = "Arbeit" };
            var gone = new Workspace { Name = "Weg", DeletedAt = DateTime.Now };
            ctx.Tasks.AddRange(
                Task("Job", WorkStatus.Done, InWeek, project: new Project { Name = "Job", Workspace = work }),
                Task("Verwaist", WorkStatus.Done, InWeek, project: new Project { Name = "Alt", Workspace = gone }));
            await ctx.SaveChangesAsync();
            workId = work.Id;
        }

        using (var act = db.NewContext())
        {
            var onlyWork = await new TaskRepository(act).GetWeeklyReportAsync(Monday, [workId], false);
            Assert.Equal(["Job"], onlyWork.Completed.Select(t => t.Title));
        }

        using (var act = db.NewContext())
        {
            var withUnassigned = await new TaskRepository(act).GetWeeklyReportAsync(Monday, [workId], true);
            Assert.Equal(["Job", "Verwaist"], withUnassigned.Completed.Select(t => t.Title).Order());
        }
    }

    // Früher verworfen, jetzt doch erledigt: Der Erledigt-Zeitpunkt ist jetzt, sonst
    // fehlte der Task im Bericht der Woche, in der er wirklich fertig wurde.
    [Fact]
    public async Task AbandonedThenDone_GetsNewCompletedAt()
    {
        using var db = new TestDatabase();
        int id;
        var abandonedAt = DateTime.Now.AddDays(-30);
        using (var ctx = db.NewContext())
        {
            var task = Task("Doch gemacht", WorkStatus.Abandoned, abandonedAt);
            ctx.Tasks.Add(task);
            await ctx.SaveChangesAsync();
            id = task.Id;
        }

        using (var act = db.NewContext())
            await new TaskRepository(act).SetStatusAsync(id, WorkStatus.Done);

        using var assert = db.NewContext();
        var completedAt = (await assert.Tasks.FindAsync(id))!.CompletedAt!.Value;
        Assert.True(completedAt > DateTime.Now.AddMinutes(-1), $"CompletedAt blieb bei {completedAt}");
    }

    [Theory]
    [InlineData("0001-01-01")]
    [InlineData("9999-12-31")]
    public async Task Weekly_WeekStartOutOfRange_IsBadRequest(string weekStart)
    {
        using var db = new TestDatabase();
        using var ctx = db.NewContext();
        var result = await new ReportsController(new TaskRepository(ctx)).Weekly(DateOnly.Parse(weekStart), null);
        Assert.IsType<Microsoft.AspNetCore.Mvc.BadRequestObjectResult>(result);
    }

    [Theory]
    [InlineData("2026-11-09", "2026-11-09")] // Montag bleibt
    [InlineData("2026-11-13", "2026-11-09")] // Freitag
    [InlineData("2026-11-15", "2026-11-09")] // Sonntag gehört zur selben Woche
    public void MondayOf_ReturnsMondayOfIsoWeek(string input, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), ReportsController.MondayOf(DateOnly.Parse(input)));

    private static TaskItem Task(
        string title, WorkStatus status, DateTime? completedAt = null,
        TaskKind kind = TaskKind.Task, Project? project = null) =>
        new() { Title = title, Status = status, CompletedAt = completedAt, Kind = kind, Project = project };
}
