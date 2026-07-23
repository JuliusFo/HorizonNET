using HorizonNET.App.Services;

namespace HorizonNET.Tests;

// Die Versatz-Regel zwischen Client- und API-Version. Verglichen wird nur die Nummer,
// nicht der angehängte Commit – sonst meldet sich der Toast beim Entwickeln dauerhaft.
public class VersionStateTests
{
    [Fact]
    public void SameNumber_DifferentCommit_IsNoMismatch()
    {
        // Der Fall aus der Praxis: Client und API aus verschiedenen Builds derselben
        // Version. Kein Grund für eine Warnung – und Neuladen könnte daran ohnehin
        // nichts ändern, weil beide Werte einkompiliert sind.
        Assert.False(VersionState.IsMismatch(
            "0.11.0+23f1566231e1719a9e307af3ac83331868dce5cb",
            "0.11.0+250c087f53cb62af92cd43021b6c00ce2a058aad"));
    }

    [Fact]
    public void DifferentNumber_IsMismatch()
    {
        // Echter Versionssprung – hier SOLL gewarnt werden.
        Assert.True(VersionState.IsMismatch("0.11.0+abc123", "0.12.0+abc123"));
    }

    [Fact]
    public void DifferentNumber_WithoutCommitSuffix_IsMismatch()
    {
        Assert.True(VersionState.IsMismatch("0.11.0", "0.12.0"));
    }

    [Fact]
    public void IdenticalVersions_IsNoMismatch()
    {
        Assert.False(VersionState.IsMismatch("0.11.0+abc123", "0.11.0+abc123"));
        Assert.False(VersionState.IsMismatch("0.11.0", "0.11.0"));
    }

    [Fact]
    public void UnknownApiVersion_IsNoMismatch()
    {
        // Antwortet die API nicht, wird nicht gewarnt – eine fehlende Auskunft ist
        // kein Versatz.
        Assert.False(VersionState.IsMismatch("0.11.0+abc123", null));
    }
}
