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
}
