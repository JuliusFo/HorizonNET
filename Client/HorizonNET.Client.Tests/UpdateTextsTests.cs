using HorizonNET.App.Services;
using HorizonNET.Shared.Transfer.Update;

namespace HorizonNET.Client.Tests;

public class UpdateTextsTests
{
    // Jede Phase, die der Updater schreiben kann, hat einen deutschen Text – kein Enum-Name in der UI.
    [Fact]
    public void Phase_HasTextForEveryPhase()
    {
        foreach (var phase in Enum.GetValues<UpdatePhase>())
            Assert.NotEqual(phase.ToString(), UpdateTexts.Phase(phase));
    }

    [Theory]
    [InlineData(UpdatePhase.Succeeded, "text-success")]
    [InlineData(UpdatePhase.RolledBack, "text-warning-emphasis")]
    [InlineData(UpdatePhase.RollbackFailed, "text-danger")]
    [InlineData(UpdatePhase.Installing, "text-muted")]
    public void PhaseCss_MarksOutcome(UpdatePhase phase, string css) =>
        Assert.Equal(css, UpdateTexts.PhaseCss(phase));

    private static readonly DateTime Before = new(2026, 11, 19, 10, 0, 0, DateTimeKind.Utc);

    // K3 (Review 2026-11-19): Ein Lauf, der zwischen zwei Abfragen beginnt und endet („kein Update
    // nötig“ nach 2 s), wurde nie als laufend gesehen – der Client wartete bis zu 10 Minuten.
    [Fact]
    public void FinishedWithoutUpdate_QuickRunNeverSeenRunning_IsReported()
    {
        var info = Info(installing: false, Run(UpdatePhase.NoUpdate, Before.AddSeconds(5)));

        Assert.Equal(UpdatePhase.NoUpdate, UpdateTexts.FinishedWithoutUpdate(info, Before, sawRunning: false)?.Phase);
    }

    [Fact]
    public void FinishedWithoutUpdate_RunFromBeforeTheRequest_IsIgnored()
    {
        var info = Info(installing: false, Run(UpdatePhase.Failed, Before));

        Assert.Null(UpdateTexts.FinishedWithoutUpdate(info, Before, sawRunning: false));
    }

    [Fact]
    public void FinishedWithoutUpdate_RunningOrSucceeded_IsNotReported()
    {
        Assert.Null(UpdateTexts.FinishedWithoutUpdate(
            Info(installing: true, Run(UpdatePhase.Downloading, Before.AddSeconds(5))), Before, sawRunning: true));
        Assert.Null(UpdateTexts.FinishedWithoutUpdate(
            Info(installing: false, Run(UpdatePhase.Succeeded, Before.AddSeconds(5))), Before, sawRunning: true));
    }

    [Fact]
    public void FinishedWithoutUpdate_FirstRunEver_IsReported()
    {
        var info = Info(installing: false, Run(UpdatePhase.RolledBack, Before));

        Assert.NotNull(UpdateTexts.FinishedWithoutUpdate(info, runBefore: null, sawRunning: false));
    }

    private static UpdateRunStatus Run(UpdatePhase phase, DateTime started) =>
        new(phase, "1.3.0", "1.4.0", "", started, started, UpdatePhases.IsRunning(phase) ? null : started);

    private static UpdateInfoDto Info(bool installing, UpdateRunStatus run) =>
        new(UpdateMode.Install, "1.3.0", null, null, null, run, installing, CanInstall: false);
}
