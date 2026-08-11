using HorizonNET.Shared.Transfer;

namespace HorizonNET.Tests;

// Die Vererbungsregel der Erinnerung steckt in einer einzigen nullable Spalte, deren
// Bedeutung man ihr nicht ansieht. Diese Tests halten sie fest – vor allem die beiden
// Fälle, die sich nur um ein Vorzeichen unterscheiden und völlig Verschiedenes bedeuten.
public class TaskReminderTests
{
    [Fact]
    public void OhneWertAmTask_GiltDerStandard()
    {
        Assert.Equal(15, TaskReminder.Effective(taskMinutes: null, defaultMinutes: 15));
        Assert.Null(TaskReminder.Effective(taskMinutes: null, defaultMinutes: null));
    }

    [Fact]
    public void WertAmTask_SchlaegtDenStandard()
    {
        Assert.Equal(30, TaskReminder.Effective(taskMinutes: 30, defaultMinutes: 15));

        // 0 heißt "zum Termin" und ist ausdrücklich KEIN "keine Erinnerung" –
        // sonst wäre die Unterscheidung zu None hinfällig.
        Assert.Equal(0, TaskReminder.Effective(taskMinutes: 0, defaultMinutes: 15));
    }

    [Fact]
    public void NoneAmTask_SchaltetTrotzStandardAb()
    {
        Assert.Null(TaskReminder.Effective(TaskReminder.None, defaultMinutes: 15));
        Assert.Null(TaskReminder.Effective(TaskReminder.None, defaultMinutes: null));
    }

    [Theory]
    [InlineData(null, true)]                 // nicht gesetzt
    [InlineData(TaskReminder.None, true)]    // ausdrücklich keine
    [InlineData(0, true)]
    [InlineData(40320, true)]                // Googles Obergrenze: vier Wochen
    [InlineData(-2, false)]                  // unterhalb des Sonderwerts
    [InlineData(40321, false)]
    public void IsValid_PruefstDenErlaubtenBereich(int? minuten, bool erwartet) =>
        Assert.Equal(erwartet, TaskReminder.IsValid(minuten));
}
