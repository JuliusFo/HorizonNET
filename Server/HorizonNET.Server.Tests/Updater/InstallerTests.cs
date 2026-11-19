using HorizonNET.Shared.Transfer.Update;
using HorizonNET.Updater;

namespace HorizonNET.Server.Tests.Updater;

public class InstallerTests
{
    [Fact]
    public async Task Apply_SwapsProgramFolders_KeepsPreviousAndBackup()
    {
        using var box = new UpdaterSandbox();
        box.Install("1.3.0");
        var package = box.CreatePackageDirectory("1.4.0");

        var exit = await box.CreateInstaller().ApplyAsync(package, CancellationToken.None);

        Assert.Equal(0, exit);
        foreach (var component in UpdaterLayout.Components)
        {
            Assert.Equal("1.4.0", box.InstalledVersion(component));
            Assert.Equal("1.3.0", File.ReadAllText(Path.Combine(box.Layout.Previous(component), "version.txt")));
        }
        Assert.Equal(["app", "tools", "updater"], box.Permissions.Reset);

        var backup = Assert.Single(Directory.GetDirectories(box.Layout.Backups, "pre-1.4.0-*"));
        Assert.Equal("db 1.3.0", File.ReadAllText(Path.Combine(backup, "data", "horizonnet.db")));
        Assert.True(File.Exists(Path.Combine(backup, "keys", "key-1.xml")));

        var status = box.State.ReadStatus();
        Assert.Equal(UpdatePhase.Succeeded, status?.Phase);
        Assert.Equal("1.3.0", status?.FromVersion);
        Assert.Equal("1.4.0", status?.TargetVersion);
        Assert.NotNull(status?.FinishedUtc);
        Assert.Null(box.State.ReadJournal());
    }

    [Fact]
    public async Task Apply_NewVersionDoesNotAnswer_RollsBackProgramAndDatabase()
    {
        using var box = new UpdaterSandbox();
        box.Install("1.3.0");
        box.Service.BrokenVersion = "1.4.0";
        var package = box.CreatePackageDirectory("1.4.0");

        var exit = await box.CreateInstaller().ApplyAsync(package, CancellationToken.None);

        Assert.Equal(1, exit);
        foreach (var component in UpdaterLayout.Components)
            Assert.Equal("1.3.0", box.InstalledVersion(component));
        // Die neue Version hatte die DB schon „migriert“; zurück kommt die Sicherung, danach
        // startet die alte Version (und schreibt in der Attrappe wieder hinein).
        Assert.Equal("db migrated by 1.3.0", File.ReadAllText(box.DbFile));
        Assert.Equal(["stop", "start", "stop", "start"], box.Service.Calls);
        Assert.True(File.Exists(Path.Combine(box.Layout.Keys, "key-1.xml")));
        Assert.Equal(UpdatePhase.RolledBack, box.State.ReadStatus()?.Phase);
        Assert.Null(box.State.ReadJournal());
    }

    [Fact]
    public async Task Apply_RollbackRestoresDatabaseFromBackup()
    {
        using var box = new UpdaterSandbox();
        box.Install("1.3.0");
        box.Service.BrokenVersion = "1.4.0";
        var package = box.CreatePackageDirectory("1.4.0");
        await box.CreateInstaller().ApplyAsync(package, CancellationToken.None);

        var backup = Assert.Single(Directory.GetDirectories(box.Layout.Backups, "pre-1.4.0-*"));
        Assert.Equal("db 1.3.0", File.ReadAllText(Path.Combine(backup, "data", "horizonnet.db")));
        Assert.DoesNotContain("1.4.0", File.ReadAllText(box.DbFile));
    }

    [Fact]
    public async Task Apply_ServiceDoesNotStop_ChangesNothing()
    {
        using var box = new UpdaterSandbox();
        box.Install("1.3.0");
        box.Service.FailStop = true;
        var package = box.CreatePackageDirectory("1.4.0");

        var exit = await box.CreateInstaller().ApplyAsync(package, CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Equal("1.3.0", box.InstalledVersion());
        Assert.False(Directory.Exists(box.Layout.Previous("app")));
        Assert.Empty(Directory.GetDirectories(box.Layout.Backups));
        Assert.Equal(UpdatePhase.Failed, box.State.ReadStatus()?.Phase);
        Assert.Null(box.State.ReadJournal());
    }

    [Fact]
    public async Task Apply_WithoutUpdaterFolderBefore_RollbackRemovesNewOne()
    {
        // Erste Version mit Updater wird von Hand eingespielt; davor gab es kein updater\.
        using var box = new UpdaterSandbox();
        box.Install("1.3.0", withUpdater: false);
        box.Service.BrokenVersion = "1.4.0";
        var package = box.CreatePackageDirectory("1.4.0");

        await box.CreateInstaller().ApplyAsync(package, CancellationToken.None);

        Assert.False(Directory.Exists(box.Layout.Component("updater")));
        Assert.Equal("1.3.0", box.InstalledVersion());
    }

    [Fact]
    public async Task Apply_ManifestVersionMismatch_IsRejected()
    {
        using var box = new UpdaterSandbox();
        box.Install("1.3.0");
        var package = box.CreatePackageDirectory("1.4.0");
        File.Delete(Path.Combine(package, "manifest.json"));

        var exit = await box.CreateInstaller().ApplyAsync(package, CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Empty(box.Service.Calls);
        Assert.Equal("1.3.0", box.InstalledVersion());
    }

    [Fact]
    public async Task Apply_KeepsOnlyTheLastFiveBackups()
    {
        using var box = new UpdaterSandbox();
        box.Install("1.3.0");
        for (var i = 0; i < 7; i++)
        {
            Directory.CreateDirectory(Path.Combine(box.Layout.Backups, $"pre-1.2.{i}-20260101-00000{i}"));
        }

        await box.CreateInstaller().ApplyAsync(box.CreatePackageDirectory("1.4.0"), CancellationToken.None);

        var left = Directory.GetDirectories(box.Layout.Backups).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(Installer.KeepBackups, left.Count);
        Assert.Contains(left, n => n!.StartsWith("pre-1.4.0-", StringComparison.Ordinal));
        Assert.Contains("pre-1.2.6-20260101-000006", left);
        Assert.DoesNotContain("pre-1.2.2-20260101-000002", left);
    }

    [Fact]
    public async Task Recover_AfterCrashMidSwap_RestoresOldProgramAndKeepsRunningUpdater()
    {
        using var box = new UpdaterSandbox();
        box.Install("1.3.0");
        // Zustand nach einem Absturz: app und updater getauscht, Dienst gestoppt, neue Version
        // nie gestartet.
        foreach (var component in new[] { "app", "updater" })
        {
            Directory.Move(box.Layout.Component(component), box.Layout.Previous(component));
            Directory.CreateDirectory(box.Layout.Component(component));
            File.WriteAllText(Path.Combine(box.Layout.Component(component), "version.txt"), "1.4.0");
        }
        var journal = new UpdateJournal
        {
            TargetVersion = "1.4.0",
            FromVersion = "1.3.0",
            ServiceStopped = true,
            Swapped = [new("app", true), new("updater", true)],
        };

        var ok = await box.CreateInstaller().RecoverAsync(journal, CancellationToken.None);

        Assert.True(ok);
        Assert.Equal("1.3.0", box.InstalledVersion());
        Assert.Equal("1.4.0", box.InstalledVersion("updater")); // läuft gerade, bleibt
        // Die neue Version lief nie: keine Rücksicherung der DB, nur der Neustart der alten.
        Assert.Equal("db migrated by 1.3.0", File.ReadAllText(box.DbFile));
        Assert.Equal(UpdatePhase.RolledBack, box.State.ReadStatus()?.Phase);
    }
}
