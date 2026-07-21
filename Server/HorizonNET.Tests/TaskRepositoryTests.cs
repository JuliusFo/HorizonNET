using HorizonNET.Data.Repositories;
using HorizonNET.Domain.Entities;
using HorizonNET.Shared.Transfer.Enums;
using Microsoft.EntityFrameworkCore;

namespace HorizonNET.Tests;

// Die Geschäftsregeln des TaskRepository. Bewusst gegen echtes SQLite (siehe TestDatabase),
// weil hier Fremdschlüssel, Cascade und Soft-Delete-Filter mitspielen. Jeder Test seedet,
// handelt und prüft über je einen frischen Kontext – so wie die App pro Request einen
// eigenen Scope bekommt.
public class TaskRepositoryTests
{
    // ── Timer-Kopplung an den Status ─────────────────────────────────────────────
    // "In Arbeit" startet die Uhr, jeder Wechsel weg davon stoppt sie. Die Regel liegt
    // im Repository, damit sie für Board, Dialog, Detailseite und Timer-Knopf gleich gilt.

    [Fact]
    public async Task SetStatus_ToInProgress_StartsTimer()
    {
        using var db = new TestDatabase();
        var id = await SeedTaskAsync(db, WorkStatus.Planned);

        using (var act = db.NewContext())
            await new TaskRepository(act).SetStatusAsync(id, WorkStatus.InProgress);

        using var assert = db.NewContext();
        var entries = await assert.TimeEntries.Where(e => e.TaskItemId == id).ToListAsync();
        var entry = Assert.Single(entries);
        Assert.Null(entry.EndedAt); // läuft
        Assert.Equal(WorkStatus.InProgress, (await assert.Tasks.FindAsync(id))!.Status);
    }

    [Fact]
    public async Task SetStatus_LeavingInProgress_StopsRunningTimer()
    {
        using var db = new TestDatabase();
        var id = await SeedTaskAsync(db, WorkStatus.InProgress, withRunningTimer: true);

        using (var act = db.NewContext())
            await new TaskRepository(act).SetStatusAsync(id, WorkStatus.Paused);

        using var assert = db.NewContext();
        var entry = await assert.TimeEntries.SingleAsync(e => e.TaskItemId == id);
        Assert.NotNull(entry.EndedAt); // gestoppt
    }

    // Höchstens eine laufende Uhr im System: Start an Task B verdrängt Task A – dessen
    // Timer wird gestoppt UND sein Status auf "Pausiert" gezogen, sonst stünden zwei Tasks
    // auf "In Arbeit", von denen nur einer läuft.
    [Fact]
    public async Task SetStatus_InProgressOnSecondTask_StopsAndPausesFirst()
    {
        using var db = new TestDatabase();
        var a = await SeedTaskAsync(db, WorkStatus.InProgress, withRunningTimer: true);
        var b = await SeedTaskAsync(db, WorkStatus.Planned);

        using (var act = db.NewContext())
            await new TaskRepository(act).SetStatusAsync(b, WorkStatus.InProgress);

        using var assert = db.NewContext();
        Assert.Equal(WorkStatus.Paused, (await assert.Tasks.FindAsync(a))!.Status);
        Assert.NotNull((await assert.TimeEntries.SingleAsync(e => e.TaskItemId == a)).EndedAt);

        Assert.Equal(WorkStatus.InProgress, (await assert.Tasks.FindAsync(b))!.Status);
        Assert.Null((await assert.TimeEntries.SingleAsync(e => e.TaskItemId == b)).EndedAt);
    }

    // ── Fälligkeit bei "Geplant Heute" ───────────────────────────────────────────
    // Der Statuswechsel setzt das Fälligkeitsdatum auf heute – aber nur beim WECHSEL,
    // sonst zöge jedes spätere Speichern eines länger geplanten Tasks sein Datum auf heute.

    [Fact]
    public async Task SetStatus_ToPlannedToday_SetsDueDateToToday()
    {
        using var db = new TestDatabase();
        var id = await SeedTaskAsync(db, WorkStatus.Planned);

        using (var act = db.NewContext())
            await new TaskRepository(act).SetStatusAsync(id, WorkStatus.PlannedToday);

        using var assert = db.NewContext();
        Assert.Equal(DateTime.Today, (await assert.Tasks.FindAsync(id))!.DueDate?.Date);
    }

    [Fact]
    public async Task Update_StaysPlannedToday_DoesNotResetDueDate()
    {
        using var db = new TestDatabase();
        var earlier = DateTime.Today.AddDays(-3);
        var id = await SeedTaskAsync(db, WorkStatus.PlannedToday, dueDate: earlier);

        // Erneutes Speichern OHNE Statuswechsel (der Dialog schickt das bestehende Datum
        // mit): Es darf NICHT auf heute hochgezogen werden – das täte ein Fehlen der
        // "nur beim Wechsel"-Bedingung in ApplyDueDateForStatusChange.
        using (var act = db.NewContext())
            await new TaskRepository(act).UpdateAsync(id, Edit(status: WorkStatus.PlannedToday, dueDate: earlier));

        using var assert = db.NewContext();
        Assert.Equal(earlier, (await assert.Tasks.FindAsync(id))!.DueDate);
    }

    // ── "Warten auf" ─────────────────────────────────────────────────────────────
    // Frisch ausgefüllt ruht der Task → "Pausiert". Aber nur beim Wechsel von leer auf
    // gefüllt: Wer trotz Warten schon anfängt (Status "In Arbeit" setzt), soll das dürfen.

    [Fact]
    public async Task Update_FillingWaitingFor_ForcesPaused()
    {
        using var db = new TestDatabase();
        var id = await SeedTaskAsync(db, WorkStatus.Planned);

        using (var act = db.NewContext())
            await new TaskRepository(act).UpdateAsync(id, Edit(status: WorkStatus.Planned, waitingFor: "Rückmeldung von Anna"));

        using var assert = db.NewContext();
        Assert.Equal(WorkStatus.Paused, (await assert.Tasks.FindAsync(id))!.Status);
    }

    [Fact]
    public async Task Update_WaitingForAlreadyFilled_KeepsRequestedStatus()
    {
        using var db = new TestDatabase();
        var id = await SeedTaskAsync(db, WorkStatus.Paused, waitingFor: "Rückmeldung von Anna");

        // "Warten auf" bleibt gesetzt, Nutzer stellt bewusst auf "In Arbeit".
        using (var act = db.NewContext())
            await new TaskRepository(act).UpdateAsync(id, Edit(status: WorkStatus.InProgress, waitingFor: "Rückmeldung von Anna"));

        using var assert = db.NewContext();
        Assert.Equal(WorkStatus.InProgress, (await assert.Tasks.FindAsync(id))!.Status);
    }

    // ── Projektwechsel zieht Sub-Tasks mit ───────────────────────────────────────
    // Sub-Tasks tragen immer das Projekt ihres Haupt-Tasks. Beim Umhängen müssen sie
    // mitwandern, sonst blieben sie im alten Projekt zurück (und gingen beim Löschen
    // jenes Projekts mit).

    [Fact]
    public async Task SetProject_MovesSubTasksToNewProject()
    {
        using var db = new TestDatabase();
        var (from, to) = await SeedTwoProjectsAsync(db);
        var (parent, sub) = await SeedParentWithSubAsync(db, projectId: from);

        using (var act = db.NewContext())
            await new TaskRepository(act).SetProjectAsync(parent, to);

        using var assert = db.NewContext();
        Assert.Equal(to, (await assert.Tasks.FindAsync(parent))!.ProjectId);
        Assert.Equal(to, (await assert.Tasks.FindAsync(sub))!.ProjectId);
    }

    [Fact]
    public async Task SetProject_ToNull_MovesSubTasksToInbox()
    {
        using var db = new TestDatabase();
        var (from, _) = await SeedTwoProjectsAsync(db);
        var (parent, sub) = await SeedParentWithSubAsync(db, projectId: from);

        using (var act = db.NewContext())
            await new TaskRepository(act).SetProjectAsync(parent, null);

        using var assert = db.NewContext();
        Assert.Null((await assert.Tasks.FindAsync(parent))!.ProjectId);
        Assert.Null((await assert.Tasks.FindAsync(sub))!.ProjectId);
    }

    // ── Seed-Helfer ──────────────────────────────────────────────────────────────

    private static async Task<int> SeedTaskAsync(
        TestDatabase db, WorkStatus status,
        bool withRunningTimer = false, DateTime? dueDate = null, string? waitingFor = null)
    {
        using var ctx = db.NewContext();
        var task = new TaskItem { Title = "Task", Status = status, DueDate = dueDate, WaitingFor = waitingFor };
        ctx.Tasks.Add(task);
        await ctx.SaveChangesAsync();

        if (withRunningTimer)
        {
            ctx.TimeEntries.Add(new TimeEntry { TaskItemId = task.Id, StartedAt = DateTime.Now.AddMinutes(-5) });
            await ctx.SaveChangesAsync();
        }

        return task.Id;
    }

    private static async Task<(int From, int To)> SeedTwoProjectsAsync(TestDatabase db)
    {
        using var ctx = db.NewContext();
        var from = new Project { Name = "Von", Status = ProjectStatus.Active, Priority = Priority.Medium };
        var to   = new Project { Name = "Nach", Status = ProjectStatus.Active, Priority = Priority.Medium };
        ctx.Projects.AddRange(from, to);
        await ctx.SaveChangesAsync();
        return (from.Id, to.Id);
    }

    private static async Task<(int Parent, int Sub)> SeedParentWithSubAsync(TestDatabase db, int projectId)
    {
        using var ctx = db.NewContext();
        var parent = new TaskItem { Title = "Haupt", Status = WorkStatus.Planned, ProjectId = projectId };
        ctx.Tasks.Add(parent);
        await ctx.SaveChangesAsync();

        var sub = new TaskItem { Title = "Sub", Status = WorkStatus.Planned, ProjectId = projectId, ParentTaskId = parent.Id };
        ctx.Tasks.Add(sub);
        await ctx.SaveChangesAsync();

        return (parent.Id, sub.Id);
    }

    // Baut den Vollersatz-Stand, den UpdateAsync erwartet. Nur die im Test relevanten
    // Felder sind Parameter; der Rest sind unschädliche Vorgaben.
    private static TaskItem Edit(WorkStatus status, string? waitingFor = null, DateTime? dueDate = null) => new()
    {
        Title = "Task",
        Priority = Priority.Medium,
        Status = status,
        WaitingFor = waitingFor,
        DueDate = dueDate
    };
}
