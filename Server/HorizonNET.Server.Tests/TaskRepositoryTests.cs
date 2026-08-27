using HorizonNET.Data.Repositories;
using HorizonNET.Domain.Entities;
using HorizonNET.Shared.Transfer.Enums;
using Microsoft.EntityFrameworkCore;

namespace HorizonNET.Server.Tests;

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

    // Das Teil-Update rührt ausschließlich den Status an. Klingt selbstverständlich, war
    // es aber nicht: Der Timer-Knopf lief einmal über den Vollersatz UpdateAsync und
    // schickte dabei nur die Felder mit, die er kannte – Link und "Warten auf" waren nach
    // einem Klick auf ▶ gelöscht. Wer SetStatusAsync je auf UpdateAsync umbaut, merkt es hier.
    [Fact]
    public async Task SetStatus_KeepsFieldsItWasNotAskedToChange()
    {
        using var db = new TestDatabase();
        var id = await SeedTaskAsync(
            db, WorkStatus.Planned, waitingFor: "Rückmeldung von Anna", link: "https://example.org/ticket/1");

        using (var act = db.NewContext())
            await new TaskRepository(act).SetStatusAsync(id, WorkStatus.InProgress);

        using var assert = db.NewContext();
        var task = (await assert.Tasks.FindAsync(id))!;
        Assert.Equal(WorkStatus.InProgress, task.Status);
        Assert.Equal("Rückmeldung von Anna", task.WaitingFor);
        Assert.Equal("https://example.org/ticket/1", task.Link);
        Assert.Equal("Task", task.Title);
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

    // ── Auswahllisten (GetOptionsAsync) ──────────────────────────────────────────
    // Die schlanke Liste für Klappfelder. Wichtig ist beides: dass alles Nötige drin ist
    // (Titel, Projekt, Sub-Tasks) und dass sie sich wie die anderen Lesepfade verhält –
    // gelöschte Tasks bleiben draußen, Sub-Tasks stehen nicht doppelt oben.

    [Fact]
    public async Task GetOptions_NestsSubTasksUnderTheirParent()
    {
        using var db = new TestDatabase();
        var (from, _) = await SeedTwoProjectsAsync(db);
        var (parent, sub) = await SeedParentWithSubAsync(db, projectId: from);

        using var act = db.NewContext();
        var options = (await new TaskRepository(act).GetOptionsAsync()).ToList();

        // Nur der Haupt-Task steht oben; der Sub-Task hängt darunter und nicht daneben.
        var option = Assert.Single(options);
        Assert.Equal(parent, option.Id);
        Assert.Equal("Haupt", option.Title);
        Assert.Equal(from, option.ProjectId);

        var subOption = Assert.Single(option.SubTasks!);
        Assert.Equal(sub, subOption.Id);
        Assert.Equal("Sub", subOption.Title);
    }

    [Fact]
    public async Task GetOptions_SortsByTitle()
    {
        using var db = new TestDatabase();
        await SeedNamedTaskAsync(db, "Zebra");
        await SeedNamedTaskAsync(db, "Anker");
        await SeedNamedTaskAsync(db, "Möbel");

        using var act = db.NewContext();
        var titles = (await new TaskRepository(act).GetOptionsAsync()).Select(o => o.Title).ToList();

        Assert.Equal(["Anker", "Möbel", "Zebra"], titles);
    }

    [Fact]
    public async Task GetOptions_IgnoresSoftDeleted()
    {
        using var db = new TestDatabase();
        var id = await SeedNamedTaskAsync(db, "Gelöscht");
        await SeedNamedTaskAsync(db, "Aktiv");

        using (var act = db.NewContext())
            await new TaskRepository(act).DeleteAsync(id);

        using var assert = db.NewContext();
        var options = await new TaskRepository(assert).GetOptionsAsync();

        Assert.Equal("Aktiv", Assert.Single(options).Title);
    }

    // Der eigentliche Zweck der Methode: Die Zeiteinträge sind der Grund, warum
    // GetAllAsync teuer ist – sie dürfen hier nicht mitkommen. Ein Task mit laufender und
    // abgeschlossener Zeit liefert dieselbe schlanke Antwort wie einer ohne.
    [Fact]
    public async Task GetOptions_DoesNotDependOnTimeEntries()
    {
        using var db = new TestDatabase();
        var id = await SeedTaskAsync(db, WorkStatus.InProgress, withRunningTimer: true);

        using (var seed = db.NewContext())
        {
            seed.TimeEntries.Add(new TimeEntry
            {
                TaskItemId = id,
                StartedAt = DateTime.Now.AddHours(-2),
                EndedAt = DateTime.Now.AddHours(-1)
            });
            await seed.SaveChangesAsync();
        }

        using var act = db.NewContext();
        var option = Assert.Single(await new TaskRepository(act).GetOptionsAsync());

        Assert.Equal(id, option.Id);
        Assert.Equal("Task", option.Title);
        Assert.Empty(option.SubTasks!);
    }

    // ── Umsortieren im Kanban-Board ──────────────────────────────────────────────
    // ReorderAsync meldet zurück, welche Tasks ein neues Fälligkeitsdatum bekommen haben.
    // Daran hängt der Google-Sync: Nur diese Tasks werden gespiegelt. Meldet die Methode
    // zu viel, kostet jedes Verschieben unnötige Netzaufrufe; meldet sie zu wenig, steht
    // im Kalender ein veralteter Termin.

    [Fact]
    public async Task Reorder_IntoPlannedToday_ReportsTaskWithNewDueDate()
    {
        using var db = new TestDatabase();
        var id = await SeedTaskAsync(db, WorkStatus.Planned);

        using var act = db.NewContext();
        var rescheduled = await new TaskRepository(act).ReorderAsync(WorkStatus.PlannedToday, [id]);

        Assert.Equal(id, Assert.Single(rescheduled).Id);
        Assert.Equal(DateTime.Today, Assert.Single(rescheduled).DueDate?.Date);
    }

    // Der eigentliche Punkt: Karten, die schon in der Spalte stehen, bekommen beim
    // Umsortieren kein neues Datum – und dürfen deshalb auch nicht gemeldet werden.
    [Fact]
    public async Task Reorder_WithinPlannedToday_ReportsNothing()
    {
        using var db = new TestDatabase();
        var a = await SeedTaskAsync(db, WorkStatus.PlannedToday, dueDate: DateTime.Today);
        var b = await SeedTaskAsync(db, WorkStatus.PlannedToday, dueDate: DateTime.Today);

        using var act = db.NewContext();
        var rescheduled = await new TaskRepository(act).ReorderAsync(WorkStatus.PlannedToday, [b, a]);

        Assert.Empty(rescheduled);
    }

    // Gemischte Spalte: Nur der Neuzugang wird gemeldet, nicht die ganze Spalte.
    [Fact]
    public async Task Reorder_IntoPlannedToday_ReportsOnlyTheNewcomer()
    {
        using var db = new TestDatabase();
        var resident = await SeedTaskAsync(db, WorkStatus.PlannedToday, dueDate: DateTime.Today);
        var newcomer = await SeedTaskAsync(db, WorkStatus.Planned);

        using var act = db.NewContext();
        var rescheduled = await new TaskRepository(act).ReorderAsync(
            WorkStatus.PlannedToday, [newcomer, resident]);

        Assert.Equal(newcomer, Assert.Single(rescheduled).Id);
    }

    // Jede andere Spalte rührt das Fälligkeitsdatum nicht an – dort ist nie etwas zu
    // spiegeln, auch wenn Status und Timer sich sehr wohl ändern.
    [Theory]
    [InlineData(WorkStatus.Planned)]
    [InlineData(WorkStatus.InProgress)]
    [InlineData(WorkStatus.Done)]
    public async Task Reorder_IntoOtherColumn_ReportsNothing(WorkStatus target)
    {
        using var db = new TestDatabase();
        var id = await SeedTaskAsync(db, WorkStatus.PlannedPriority);

        using var act = db.NewContext();
        var rescheduled = await new TaskRepository(act).ReorderAsync(target, [id]);

        Assert.Empty(rescheduled);
    }

    // Die eigentliche Aufgabe der Methode darf darüber nicht verloren gehen.
    [Fact]
    public async Task Reorder_AppliesPositionAndStatus()
    {
        using var db = new TestDatabase();
        var a = await SeedTaskAsync(db, WorkStatus.Planned);
        var b = await SeedTaskAsync(db, WorkStatus.Planned);

        using (var act = db.NewContext())
            await new TaskRepository(act).ReorderAsync(WorkStatus.Done, [b, a]);

        using var assert = db.NewContext();
        Assert.Equal(0, (await assert.Tasks.FindAsync(b))!.SortOrder);
        Assert.Equal(1, (await assert.Tasks.FindAsync(a))!.SortOrder);
        Assert.Equal(WorkStatus.Done, (await assert.Tasks.FindAsync(a))!.Status);
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

    // ── Erledigt-Zeitstempel (Phase 14h) ─────────────────────────────────────────
    // Trägt den Tagesrückblick im Journal. UpdatedAt taugte dafür nicht: Ein späteres
    // Umbenennen hätte den Task in der Rückschau auf den falschen Tag verschoben.

    [Fact]
    public async Task SetStatus_ToDone_SetsCompletedAt()
    {
        using var db = new TestDatabase();
        var id = await SeedTaskAsync(db, WorkStatus.Planned);

        using (var act = db.NewContext())
            await new TaskRepository(act).SetStatusAsync(id, WorkStatus.Done);

        using var assert = db.NewContext();
        Assert.NotNull((await assert.Tasks.FindAsync(id))!.CompletedAt);
    }

    [Fact]
    public async Task SetStatus_ReopeningDoneTask_ClearsCompletedAt()
    {
        using var db = new TestDatabase();
        var id = await SeedTaskAsync(db, WorkStatus.Planned);

        using (var act = db.NewContext())
            await new TaskRepository(act).SetStatusAsync(id, WorkStatus.Done);

        using (var act = db.NewContext())
            await new TaskRepository(act).SetStatusAsync(id, WorkStatus.InProgress);

        using var assert = db.NewContext();
        Assert.Null((await assert.Tasks.FindAsync(id))!.CompletedAt);
    }

    // Der entscheidende Test: Nur der WECHSEL setzt den Zeitstempel. Sonst wäre die
    // Spalte genauso unzuverlässig wie das UpdatedAt, das sie ersetzen soll.
    [Fact]
    public async Task SetStatus_DoneTwice_KeepsFirstCompletedAt()
    {
        using var db = new TestDatabase();
        var id = await SeedTaskAsync(db, WorkStatus.Planned);

        DateTime first;
        using (var act = db.NewContext())
            first = (await new TaskRepository(act).SetStatusAsync(id, WorkStatus.Done))!.CompletedAt!.Value;

        await Task.Delay(20);

        using (var act = db.NewContext())
            await new TaskRepository(act).SetStatusAsync(id, WorkStatus.Done);

        using var assert = db.NewContext();
        Assert.Equal(first, (await assert.Tasks.FindAsync(id))!.CompletedAt);
    }

    [Fact]
    public async Task GetCompletedOn_ReturnsOnlyThatDay()
    {
        using var db = new TestDatabase();
        var heute = await SeedTaskAsync(db, WorkStatus.Planned);
        var gestern = await SeedTaskAsync(db, WorkStatus.Planned);

        using (var act = db.NewContext())
        {
            var repo = new TaskRepository(act);
            await repo.SetStatusAsync(heute, WorkStatus.Done);
            await repo.SetStatusAsync(gestern, WorkStatus.Done);
        }

        // Einen der beiden auf gestern zurückdatieren.
        using (var act = db.NewContext())
        {
            var task = await act.Tasks.FindAsync(gestern);
            task!.CompletedAt = DateTime.Now.AddDays(-1);
            await act.SaveChangesAsync();
        }

        using var assert = db.NewContext();
        var treffer = await new TaskRepository(assert)
            .GetCompletedOnAsync(DateOnly.FromDateTime(DateTime.Now));

        Assert.Equal([heute], treffer.Select(t => t.Id));
    }

    // ── Sub-Tasks mit abschließen (Rückfrage im Client) ─────────────────────────
    // Die Projektkarte zählt Haupt- UND Sub-Tasks. Bejaht der Nutzer die Rückfrage,
    // nimmt der Abschluss des Haupt-Tasks seine offenen Sub-Tasks mit – über denselben
    // ApplyStatusChange-Weg (CompletedAt, Timer). Ohne Flag bleibt alles wie bisher.

    [Fact]
    public async Task SetStatus_DoneWithCompleteSubTasks_CompletesOpenSubTasks()
    {
        using var db = new TestDatabase();
        var parent = await SeedTaskAsync(db, WorkStatus.Planned);
        var open   = await SeedSubTaskAsync(db, parent, WorkStatus.InProgress);

        using (var act = db.NewContext())
            await new TaskRepository(act).SetStatusAsync(parent, WorkStatus.Done, completeSubTasks: true);

        using var assert = db.NewContext();
        var sub = (await assert.Tasks.FindAsync(open))!;
        Assert.Equal(WorkStatus.Done, sub.Status);
        Assert.NotNull(sub.CompletedAt);
    }

    // Ein längst erledigter Sub-Task darf dabei nicht auf heute umdatieren – er geht
    // gar nicht erst durch den Statuswechsel (nur OFFENE Sub-Tasks werden angefasst).
    [Fact]
    public async Task SetStatus_DoneWithCompleteSubTasks_KeepsCompletedSubTasksUntouched()
    {
        using var db = new TestDatabase();
        var parent  = await SeedTaskAsync(db, WorkStatus.Planned);
        var earlier = DateTime.Now.AddDays(-3);
        var done    = await SeedSubTaskAsync(db, parent, WorkStatus.Done, completedAt: earlier);

        using (var act = db.NewContext())
            await new TaskRepository(act).SetStatusAsync(parent, WorkStatus.Done, completeSubTasks: true);

        using var assert = db.NewContext();
        Assert.Equal(earlier, (await assert.Tasks.FindAsync(done))!.CompletedAt);
    }

    [Fact]
    public async Task SetStatus_DoneWithoutCompleteSubTasks_LeavesSubTasksOpen()
    {
        using var db = new TestDatabase();
        var parent = await SeedTaskAsync(db, WorkStatus.Planned);
        var open   = await SeedSubTaskAsync(db, parent, WorkStatus.Planned);

        using (var act = db.NewContext())
            await new TaskRepository(act).SetStatusAsync(parent, WorkStatus.Done);

        using var assert = db.NewContext();
        Assert.Equal(WorkStatus.Planned, (await assert.Tasks.FindAsync(open))!.Status);
    }

    // Der Weg über ApplyStatusChange ist kein Selbstzweck: Er stoppt auch die laufende
    // Uhr eines Sub-Tasks in Arbeit – sonst liefe sie unter einem erledigten Task weiter.
    [Fact]
    public async Task SetStatus_DoneWithCompleteSubTasks_StopsRunningSubTaskTimer()
    {
        using var db = new TestDatabase();
        var parent = await SeedTaskAsync(db, WorkStatus.Planned);
        var sub    = await SeedSubTaskAsync(db, parent, WorkStatus.InProgress, withRunningTimer: true);

        using (var act = db.NewContext())
            await new TaskRepository(act).SetStatusAsync(parent, WorkStatus.Done, completeSubTasks: true);

        using var assert = db.NewContext();
        Assert.NotNull((await assert.TimeEntries.SingleAsync(e => e.TaskItemId == sub)).EndedAt);
        Assert.Equal(WorkStatus.Done, (await assert.Tasks.FindAsync(sub))!.Status);
    }

    // Beim Kanban-Zug in die Fertig-Spalte nehmen nur NEU abgeschlossene Tasks ihre
    // Sub-Tasks mit – wer schon in der Spalte steht, wechselt nicht und bleibt unberührt.
    [Fact]
    public async Task Reorder_WithCompleteSubTasks_TouchesOnlyNewlyCompletedTasks()
    {
        using var db = new TestDatabase();
        var moved      = await SeedTaskAsync(db, WorkStatus.Planned);
        var movedSub   = await SeedSubTaskAsync(db, moved, WorkStatus.Planned);
        var already    = await SeedTaskAsync(db, WorkStatus.Done);
        var alreadySub = await SeedSubTaskAsync(db, already, WorkStatus.Planned);

        using (var act = db.NewContext())
            await new TaskRepository(act).ReorderAsync(WorkStatus.Done, [already, moved], completeSubTasks: true);

        using var assert = db.NewContext();
        Assert.Equal(WorkStatus.Done, (await assert.Tasks.FindAsync(movedSub))!.Status);
        Assert.Equal(WorkStatus.Planned, (await assert.Tasks.FindAsync(alreadySub))!.Status);
    }

    private static async Task<int> SeedTaskAsync(
        TestDatabase db, WorkStatus status,
        bool withRunningTimer = false, DateTime? dueDate = null,
        string? waitingFor = null, string? link = null)
    {
        using var ctx = db.NewContext();
        var task = new TaskItem
        {
            Title = "Task", Status = status, DueDate = dueDate, WaitingFor = waitingFor, Link = link
        };
        ctx.Tasks.Add(task);
        await ctx.SaveChangesAsync();

        if (withRunningTimer)
        {
            ctx.TimeEntries.Add(new TimeEntry { TaskItemId = task.Id, StartedAt = DateTime.Now.AddMinutes(-5) });
            await ctx.SaveChangesAsync();
        }

        return task.Id;
    }

    private static async Task<int> SeedSubTaskAsync(
        TestDatabase db, int parentId, WorkStatus status,
        DateTime? completedAt = null, bool withRunningTimer = false)
    {
        using var ctx = db.NewContext();
        var sub = new TaskItem { Title = "Sub", Status = status, ParentTaskId = parentId, CompletedAt = completedAt };
        ctx.Tasks.Add(sub);
        await ctx.SaveChangesAsync();

        if (withRunningTimer)
        {
            ctx.TimeEntries.Add(new TimeEntry { TaskItemId = sub.Id, StartedAt = DateTime.Now.AddMinutes(-5) });
            await ctx.SaveChangesAsync();
        }

        return sub.Id;
    }

    private static async Task<int> SeedNamedTaskAsync(TestDatabase db, string title)
    {
        using var ctx = db.NewContext();
        var task = new TaskItem { Title = title, Status = WorkStatus.Planned };
        ctx.Tasks.Add(task);
        await ctx.SaveChangesAsync();
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
