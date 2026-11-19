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
        Assert.False(Directory.Exists(box.Layout.Component("updater") + ".new"));

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
        Assert.Equal(
            ["starttype Disabled", "stop", "starttype Automatic", "start",
             "starttype Disabled", "stop", "starttype Automatic", "start"],
            box.Service.Calls);
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
        // Starttyp wieder wie vorher, Dienst wieder an.
        Assert.Equal(ServiceStartType.Automatic, box.Service.StartType);
        Assert.Equal("start", box.Service.Calls[^1]);
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
        box.Service.Running = false;
        box.Service.StartType = ServiceStartType.Disabled;
        var journal = new UpdateJournal
        {
            TargetVersion = "1.4.0",
            FromVersion = "1.3.0",
            OriginalStartType = ServiceStartType.Automatic,
            ServiceStopped = true,
            Swapped = [new("app", true), new("updater", true)],
        };

        var ok = await box.CreateInstaller().RecoverAsync(journal, CancellationToken.None);

        Assert.True(ok);
        Assert.Equal("1.3.0", box.InstalledVersion());
        Assert.Equal("1.4.0", box.InstalledVersion("updater")); // läuft gerade, bleibt
        // Die neue Version lief nie: keine Rücksicherung der DB, nur der Neustart der alten.
        Assert.Equal("db migrated by 1.3.0", File.ReadAllText(box.DbFile));
        Assert.Empty(box.Permissions.Copied);
        Assert.Equal(ServiceStartType.Automatic, box.Service.StartType);
        Assert.Equal(UpdatePhase.RolledBack, box.State.ReadStatus()?.Phase);
    }

    // M1 (Review 2026-11-19): Ein Neustart mitten im Tausch darf die neue Version nicht per
    // Autostart hochfahren (Migrationen ohne Rücksicherung). Deaktiviert bis kurz vor dem Start.
    [Fact]
    public async Task Apply_KeepsServiceDisabledUntilNewVersionStarts()
    {
        using var box = new UpdaterSandbox();
        box.Install("1.3.0");

        await box.CreateInstaller().ApplyAsync(box.CreatePackageDirectory("1.4.0"), CancellationToken.None);

        Assert.Equal(["starttype Disabled", "stop", "starttype Automatic", "start"], box.Service.Calls);
        Assert.Equal(ServiceStartType.Automatic, box.Service.StartType);
    }

    // M1: Absturz während des Stoppens – ServiceStopped stand noch nicht im Journal. Früher blieb
    // der Dienst danach aus.
    [Fact]
    public async Task Recover_AfterCrashWhileStopping_EnablesAndStartsService()
    {
        using var box = new UpdaterSandbox();
        box.Install("1.3.0");
        box.Service.Running = false;
        box.Service.StartType = ServiceStartType.Disabled;
        var journal = new UpdateJournal
        {
            TargetVersion = "1.4.0",
            FromVersion = "1.3.0",
            OriginalStartType = ServiceStartType.Automatic,
        };

        var ok = await box.CreateInstaller().RecoverAsync(journal, CancellationToken.None);

        Assert.True(ok);
        Assert.True(box.Service.Running);
        Assert.Equal(ServiceStartType.Automatic, box.Service.StartType);
        Assert.Equal("1.3.0", box.InstalledVersion());
    }

    // S2 (Review 2026-11-19): data\ und keys\ kann das Dienstkonto beschreiben. Gesichert werden
    // nur DB und Schlüsselring, keine Unterordner und keine fremden Dateien.
    [Fact]
    public async Task Apply_BacksUpOnlyDatabaseAndKeyRing()
    {
        using var box = new UpdaterSandbox();
        box.Install("1.3.0");
        File.WriteAllText(Path.Combine(box.Layout.Data, "horizonnet.db-wal"), "wal");
        File.WriteAllText(Path.Combine(box.Layout.Data, "fremd.txt"), "fremd");
        Directory.CreateDirectory(Path.Combine(box.Layout.Data, "unter"));
        File.WriteAllText(Path.Combine(box.Layout.Data, "unter", "horizonnet.db"), "unter");
        File.WriteAllText(Path.Combine(box.Layout.Keys, "notiz.txt"), "fremd");

        await box.CreateInstaller().ApplyAsync(box.CreatePackageDirectory("1.4.0"), CancellationToken.None);

        var backup = Assert.Single(Directory.GetDirectories(box.Layout.Backups, "pre-1.4.0-*"));
        Assert.Equal(["horizonnet.db", "horizonnet.db-wal"],
            Directory.GetFiles(Path.Combine(backup, "data")).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Empty(Directory.GetDirectories(Path.Combine(backup, "data")));
        Assert.Equal(["key-1.xml"], Directory.GetFiles(Path.Combine(backup, "keys")).Select(Path.GetFileName));
    }

    // S2: Ein Verweis in data\ (hier ein Symlink auf eine Datei außerhalb) bricht die Sicherung ab,
    // bevor etwas getauscht ist. Symlinks brauchen unter Windows Rechte; ohne sie prüft das die CI.
    [Fact]
    public async Task Apply_LinkInData_AbortsBeforeSwapping()
    {
        using var box = new UpdaterSandbox();
        box.Install("1.3.0");
        var secret = Path.Combine(box.Root, "nur-system.txt");
        File.WriteAllText(secret, "geheim");
        try
        {
            File.CreateSymbolicLink(Path.Combine(box.Layout.Data, "horizonnet.db-wal"), secret);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // Windows ohne Entwicklermodus
        }

        var exit = await box.CreateInstaller().ApplyAsync(box.CreatePackageDirectory("1.4.0"), CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Equal("1.3.0", box.InstalledVersion());
        Assert.False(Directory.Exists(box.Layout.Previous("app")));
        var status = box.State.ReadStatus();
        Assert.Equal(UpdatePhase.RolledBack, status?.Phase);
        Assert.Contains("Reparse Point", status?.Message);
        var backup = Assert.Single(Directory.GetDirectories(box.Layout.Backups, "pre-1.4.0-*"));
        Assert.DoesNotContain(Directory.GetFiles(backup, "*", SearchOption.AllDirectories),
            f => File.ReadAllText(f) == "geheim");
        Assert.True(box.Service.Running);
    }

    // S2: Zurückgespielt wird in einen neuen Ordner mit den Rechten des alten, der alte Stand
    // bleibt als data.failed-… liegen. Nichts wird in den bestehenden Ordner geschrieben.
    [Fact]
    public async Task Rollback_RestoresIntoFreshFolderWithCopiedRights()
    {
        using var box = new UpdaterSandbox();
        box.Install("1.3.0");
        box.Service.BrokenVersion = "1.4.0";

        await box.CreateInstaller().ApplyAsync(box.CreatePackageDirectory("1.4.0"), CancellationToken.None);

        Assert.Collection(box.Permissions.Copied,
            c => { Assert.Equal("data", c.Source); Assert.StartsWith("data.restore-", c.Target, StringComparison.Ordinal); },
            c => { Assert.Equal("keys", c.Source); Assert.StartsWith("keys.restore-", c.Target, StringComparison.Ordinal); });
        var failed = Assert.Single(Directory.GetDirectories(box.Root, "data.failed-*"));
        Assert.Equal("db migrated by 1.4.0", File.ReadAllText(Path.Combine(failed, "horizonnet.db")));
        Assert.Empty(Directory.GetDirectories(box.Root, "*.restore-*"));
        Assert.True(File.Exists(Path.Combine(box.Layout.Keys, "key-1.xml")));
    }

    // Zweiter Durchgang N1: .failed-Ordner trägt die Rechte des Dienstkontos. Ein späteres Zurückrollen
    // löscht ihn nicht (rekursives Löschen als SYSTEM ließe sich per Junction umlenken).
    [Fact]
    public async Task Rollback_NeverDeletesEarlierFailedFolders()
    {
        using var box = new UpdaterSandbox();
        box.Install("1.3.0");
        var earlier = Path.Combine(box.Root, "data.failed-20260101-000000-aaaaaa");
        Directory.CreateDirectory(Path.Combine(earlier, "unter"));
        File.WriteAllText(Path.Combine(earlier, "unter", "x.txt"), "bleibt");
        box.Service.BrokenVersion = "1.4.0";

        await box.CreateInstaller().ApplyAsync(box.CreatePackageDirectory("1.4.0"), CancellationToken.None);

        Assert.Equal("bleibt", File.ReadAllText(Path.Combine(earlier, "unter", "x.txt")));
        Assert.Equal(2, Directory.GetDirectories(box.Root, "data.failed-*").Length);
        Assert.Equal(UpdatePhase.RolledBack, box.State.ReadStatus()?.Phase);
    }

    // Zweiter Durchgang N3: Absturz zwischen „data → data.failed-…“ und „restore → data“. Der nächste
    // Lauf übernimmt die Rechte vom jüngsten .failed-Ordner statt Root-Rechte (DB schreibgeschützt).
    [Fact]
    public async Task Recover_DataFolderMissing_TakesRightsFromLatestFailedFolder()
    {
        using var box = new UpdaterSandbox();
        box.Install("1.3.0");
        var backup = Path.Combine(box.Layout.Backups, "pre-1.4.0-20260101-000000");
        Directory.CreateDirectory(Path.Combine(backup, "data"));
        File.WriteAllText(Path.Combine(backup, "data", "horizonnet.db"), "db 1.3.0");
        Directory.CreateDirectory(Path.Combine(box.Root, "data.failed-20250101-000000-aaaaaa"));
        Directory.Move(box.Layout.Data, Path.Combine(box.Root, "data.failed-20260101-000000-bbbbbb"));
        var journal = new UpdateJournal
        {
            TargetVersion = "1.4.0",
            FromVersion = "1.3.0",
            OriginalStartType = ServiceStartType.Automatic,
            ServiceStopped = true,
            BackupDirectory = backup,
            NewVersionStarted = true,
        };

        var ok = await box.CreateInstaller().RecoverAsync(journal, CancellationToken.None);

        Assert.True(ok);
        Assert.Contains(box.Permissions.Copied, c => c.Source == "data.failed-20260101-000000-bbbbbb");
        Assert.True(File.Exists(box.DbFile));
    }

    // Zweiter Durchgang N2: Stürzt die neue Version beim Start ab, startet Windows sie per Fehleraktion
    // neu, solange der Dienst nicht deaktiviert ist. Das Zurückrollen deaktiviert ihn vor dem Stoppen.
    [Fact]
    public async Task Rollback_DisablesServiceBeforeStopping()
    {
        using var box = new UpdaterSandbox();
        box.Install("1.3.0");
        box.Service.BrokenVersion = "1.4.0";
        box.Service.RestartsWhenEnabled = true;

        var exit = await box.CreateInstaller().ApplyAsync(box.CreatePackageDirectory("1.4.0"), CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Equal(UpdatePhase.RolledBack, box.State.ReadStatus()?.Phase);
        Assert.Equal("1.3.0", box.InstalledVersion());
        Assert.Equal(ServiceStartType.Automatic, box.Service.StartType);
    }

    // Dritter Durchgang N8: Das Deaktivieren ist nur ein Zusatzschutz. Scheitert es beim Zurückrollen,
    // wird trotzdem zurückgerollt (früher: RollbackFailed, Programm und DB blieben auf dem neuen Stand).
    [Fact]
    public async Task Rollback_DisableFails_StillRollsBack()
    {
        using var box = new UpdaterSandbox();
        box.Install("1.3.0");
        box.Service.BrokenVersion = "1.4.0";
        box.Service.FailDisableAt = 2; // das erste Deaktivieren (apply) klappt, das zweite nicht

        var exit = await box.CreateInstaller().ApplyAsync(box.CreatePackageDirectory("1.4.0"), CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Equal(UpdatePhase.RolledBack, box.State.ReadStatus()?.Phase);
        Assert.Equal("1.3.0", box.InstalledVersion());
        Assert.Equal("db migrated by 1.3.0", File.ReadAllText(box.DbFile));
        Assert.Equal(ServiceStartType.Automatic, box.Service.StartType);
    }

    // Zweiter Durchgang N5: Ein deaktivierter Dienst (etwa nach gescheitertem Zurückrollen) wird
    // weder angefasst noch gestartet; die Meldung sagt, was los ist.
    [Fact]
    public async Task Apply_DisabledService_ChangesNothing()
    {
        using var box = new UpdaterSandbox();
        box.Install("1.3.0");
        box.Service.StartType = ServiceStartType.Disabled;

        var exit = await box.CreateInstaller().ApplyAsync(box.CreatePackageDirectory("1.4.0"), CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Empty(box.Service.Calls);
        Assert.Null(box.State.ReadJournal());
        Assert.Contains("deaktiviert", box.State.ReadStatus()?.Message);
    }

    // K7 (Review 2026-11-19): Ohne backups\ (eigene Rechte aus server-install.ps1) kein Update.
    [Fact]
    public async Task Apply_WithoutBackupsFolder_ChangesNothing()
    {
        using var box = new UpdaterSandbox();
        box.Install("1.3.0");
        Directory.Delete(box.Layout.Backups);

        var exit = await box.CreateInstaller().ApplyAsync(box.CreatePackageDirectory("1.4.0"), CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Empty(box.Service.Calls);
        Assert.False(Directory.Exists(box.Layout.Backups));
        Assert.Contains("server-install.ps1", box.State.ReadStatus()?.Message);
    }
}
