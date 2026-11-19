using HorizonNET.Shared.Transfer.Update;
using HorizonNET.Updater;

namespace HorizonNET.Server.Tests.Updater;

public class PreparerTests
{
    [Fact]
    public async Task Run_WithoutRequest_DoesNothing()
    {
        using var box = new UpdaterSandbox();
        box.Install("1.3.0");
        var source = new LocalReleaseSource(box.CreatePackageZip("1.4.0"));

        var exit = await box.CreatePreparer(source).RunAsync(installRequested: false, CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Null(box.Launcher.Started);
        Assert.Null(box.State.ReadStatus());
    }

    [Fact]
    public async Task Run_WithInstallRequest_StagesVerifiedPackageAndStartsApply()
    {
        using var box = new UpdaterSandbox();
        box.Install("1.3.0");
        box.RequestInstall();
        var source = new LocalReleaseSource(box.CreatePackageZip("1.4.0"));

        var exit = await box.CreatePreparer(source).RunAsync(installRequested: false, CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.False(File.Exists(box.Layout.RequestFile));
        var started = Assert.NotNull(box.Launcher.Started);
        var staging = Path.Combine(box.Layout.Staging, "1.4.0");
        Assert.Equal(staging, started.Package);
        Assert.Equal(Path.Combine(staging, "updater", "HorizonNET.Updater.exe"), started.Exe);
        Assert.Equal(box.Root, started.Root);
        Assert.True(File.Exists(Path.Combine(staging, "manifest.json")));
        Assert.Empty(Directory.GetFiles(box.Layout.Downloads));
        // Noch nichts installiert – das macht apply.
        Assert.Equal("1.3.0", box.InstalledVersion());
        Assert.Equal(UpdatePhase.Installing, box.State.ReadStatus()?.Phase);
    }

    [Fact]
    public async Task Install_PicksHighestPackageInSource()
    {
        using var box = new UpdaterSandbox();
        box.Install("1.3.0");
        box.CreatePackageZip("1.3.5");
        var source = new LocalReleaseSource(box.CreatePackageZip("1.4.0"));

        await box.CreatePreparer(source).RunAsync(installRequested: true, CancellationToken.None);

        Assert.EndsWith("1.4.0", box.Launcher.Started?.Package);
    }

    [Fact]
    public async Task Install_SameVersion_ReportsNoUpdate()
    {
        using var box = new UpdaterSandbox();
        box.Install("1.4.0");
        var source = new LocalReleaseSource(box.CreatePackageZip("1.4.0"));

        var exit = await box.CreatePreparer(source).RunAsync(installRequested: true, CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Null(box.Launcher.Started);
        Assert.Equal(UpdatePhase.NoUpdate, box.State.ReadStatus()?.Phase);
    }

    [Fact]
    public async Task Install_ChecksumMismatch_FailsWithoutStaging()
    {
        using var box = new UpdaterSandbox();
        box.Install("1.3.0");
        var source = new LocalReleaseSource(box.CreatePackageZip("1.4.0", corruptChecksum: true));

        var exit = await box.CreatePreparer(source).RunAsync(installRequested: true, CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Null(box.Launcher.Started);
        Assert.Empty(Directory.GetDirectories(box.Layout.Staging));
        var status = box.State.ReadStatus();
        Assert.Equal(UpdatePhase.Failed, status?.Phase);
        Assert.Contains("Prüfsumme", status?.Message);
    }

    [Fact]
    public async Task Run_WithOpenJournal_RollsBackBeforeAnythingElse()
    {
        using var box = new UpdaterSandbox();
        box.Install("1.3.0");
        Directory.Move(box.Layout.Component("app"), box.Layout.Previous("app"));
        Directory.CreateDirectory(box.Layout.Component("app"));
        File.WriteAllText(Path.Combine(box.Layout.Component("app"), "version.txt"), "1.4.0");
        box.State.WriteJournal(new UpdateJournal
        {
            TargetVersion = "1.4.0",
            FromVersion = "1.3.0",
            ServiceStopped = true,
            Swapped = [new("app", true)],
        });

        var exit = await box.CreatePreparer(new LocalReleaseSource(box.Root)).RunAsync(false, CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Equal("1.3.0", box.InstalledVersion());
        Assert.Null(box.State.ReadJournal());
        Assert.Equal(UpdatePhase.RolledBack, box.State.ReadStatus()?.Phase);
    }

    [Fact]
    public async Task Run_UnreadableRequest_IsDiscarded()
    {
        using var box = new UpdaterSandbox();
        box.Install("1.3.0");
        File.WriteAllText(box.Layout.RequestFile, "kein json");
        var source = new LocalReleaseSource(box.CreatePackageZip("1.4.0"));

        var exit = await box.CreatePreparer(source).RunAsync(false, CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.False(File.Exists(box.Layout.RequestFile));
        Assert.Null(box.Launcher.Started);
    }
}
