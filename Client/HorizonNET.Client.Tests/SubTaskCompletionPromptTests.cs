using HorizonNET.App.Services;
using HorizonNET.Shared.Transfer.DTOs;
using HorizonNET.Shared.Transfer.Enums;

namespace HorizonNET.Client.Tests;

// Die Rückfrage beim Abschließen eines Haupt-Tasks mit offenen Sub-Tasks. Entscheidend
// sind die Wächter: Der Aufruf steht an JEDER Abschluss-Stelle (Liste, Kanban, Heute,
// Kalender, Dialog) bedingungslos – ein Dialog, der auch ohne offene Sub-Tasks erschiene,
// würde jedes simple Abhaken zur Klickstrecke machen.
public class SubTaskCompletionPromptTests
{
    [Fact]
    public async Task OhneOffeneSubTasks_KeinDialog_KeineKaskade()
    {
        var confirm = new ConfirmService();
        var task = T(1, WorkStatus.Planned, subs: [T(2, WorkStatus.Done, parentId: 1)]);

        var result = await SubTaskCompletionPrompt.AskAsync(confirm, task, WorkStatus.Done);

        Assert.False(result);
        Assert.Null(confirm.Current); // es wurde gar kein Dialog geöffnet
    }

    [Fact]
    public async Task NichtAbschliessenderStatuswechsel_KeinDialog()
    {
        var confirm = new ConfirmService();
        var task = T(1, WorkStatus.Planned, subs: [T(2, WorkStatus.Planned, parentId: 1)]);

        Assert.False(await SubTaskCompletionPrompt.AskAsync(confirm, task, WorkStatus.InProgress));
        Assert.Null(confirm.Current);
    }

    // Ein Sub-Task hat selbst keine Sub-Tasks – und ein bereits abgeschlossener Task
    // wird nur umbenannt/umgehängt, nicht erneut abgeschlossen.
    [Fact]
    public async Task SubTaskOderSchonAbgeschlossen_KeinDialog()
    {
        var confirm = new ConfirmService();
        var sub = T(2, WorkStatus.Planned, parentId: 1);
        var fertig = T(1, WorkStatus.Done, subs: [T(2, WorkStatus.Planned, parentId: 1)]);

        Assert.False(await SubTaskCompletionPrompt.AskAsync(confirm, sub, WorkStatus.Done));
        Assert.False(await SubTaskCompletionPrompt.AskAsync(confirm, fertig, WorkStatus.Abandoned));
        Assert.Null(confirm.Current);
    }

    [Fact]
    public async Task OffeneSubTasks_Bejaht_LiefertTrue()
    {
        var confirm = new ConfirmService();
        var task = T(1, WorkStatus.Planned, subs:
            [T(2, WorkStatus.Planned, parentId: 1), T(3, WorkStatus.Done, parentId: 1)]);

        var pending = SubTaskCompletionPrompt.AskAsync(confirm, task, WorkStatus.Done);

        // Der Dialog steht mit beiden echten Ausgängen – "Abbrechen" gibt es hier
        // bewusst nicht (der Statuswechsel selbst steht nicht zur Debatte).
        Assert.NotNull(confirm.Current);
        Assert.Equal("Mit Sub-Tasks abschließen", confirm.Current!.ConfirmLabel);
        Assert.Equal("Nur diesen Task", confirm.Current!.CancelLabel);

        confirm.Respond(true);
        Assert.True(await pending);
    }

    [Fact]
    public async Task OffeneSubTasks_NurDieserTask_LiefertFalse()
    {
        var confirm = new ConfirmService();
        var task = T(1, WorkStatus.Planned, subs: [T(2, WorkStatus.InProgress, parentId: 1)]);

        var pending = SubTaskCompletionPrompt.AskAsync(confirm, task, WorkStatus.Done);
        confirm.Respond(false);

        Assert.False(await pending);
    }

    private static TaskResponseDto T(int id, WorkStatus status,
        List<TaskResponseDto>? subs = null, int? parentId = null) =>
        new(id, "Task", null, null, null, null, status, "Medium", null, null, null, null,
            ParentTaskId: parentId, SubTasks: subs);
}
