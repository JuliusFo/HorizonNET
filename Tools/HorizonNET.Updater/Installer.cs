using System.Globalization;
using HorizonNET.Shared.Transfer.Update;

namespace HorizonNET.Updater;

// apply: läuft aus dem NEUEN Paket (update\staging\X.Y.Z\updater\), damit die Installationslogik
// der neuen Version gilt und updater\ ersetzt werden kann. Schritte (Journal nach jedem):
//   Dienst stoppen → data\ und keys\ sichern → app\, tools\, updater\ tauschen (alter Stand nach
//   *.previous) → Dienst starten → /api/version == neue Version?
// Scheitert etwas nach dem Stoppen, rollt es zurück: Ordner aus *.previous, und wenn die neue
// Version schon gestartet war (Migrationen!), auch data\ und keys\ aus der Sicherung.
internal sealed class Installer(
    UpdaterLayout layout,
    StateFiles state,
    IServiceHost service,
    IHealthProbe health,
    IPermissions permissions,
    IInstalledVersion installed,
    UpdaterTimings timings,
    TimeProvider time,
    UpdateLog log)
{
    public const int KeepBackups = 5;

    private readonly FileOps _files = new(timings);

    public async Task<int> ApplyAsync(string packageDirectory, CancellationToken ct)
    {
        using var updateLock = await UpdateLock.TryAcquireAsync(layout.LockFile, timings.LockWait, time);
        var status = new StatusReporter(state, time, log);
        if (updateLock is null)
        {
            status.Report(UpdatePhase.Failed, "Ein anderer Updater-Lauf hält die Sperre.");
            return 1;
        }

        Version target;
        try
        {
            target = PackageFiles.Validate(packageDirectory, expected: null);
        }
        catch (UpdateException ex)
        {
            status.Report(UpdatePhase.Failed, ex.Message);
            return 1;
        }

        var from = installed.Read(layout);
        status.FromVersion = from is null ? null : UpdatePackage.Format(from);
        status.TargetVersion = UpdatePackage.Format(target);

        var journal = new UpdateJournal
        {
            TargetVersion = status.TargetVersion,
            FromVersion = status.FromVersion,
            StartedUtc = time.GetUtcNow().UtcDateTime,
        };
        state.WriteJournal(journal);
        status.Report(UpdatePhase.Installing, $"Installiere {status.TargetVersion}: Dienst wird gestoppt.");

        try
        {
            service.Stop(timings.ServiceTimeout);
        }
        catch (Exception ex)
        {
            // Noch nichts geändert: Journal weg, Dienst möglichst wieder an.
            state.DeleteJournal();
            TryStartService();
            status.Report(UpdatePhase.Failed, $"Der Dienst ließ sich nicht stoppen ({ex.Message}). Nichts geändert.");
            return 1;
        }

        journal.ServiceStopped = true;
        state.WriteJournal(journal);

        try
        {
            journal.BackupDirectory = Backup(target);
            state.WriteJournal(journal);
            log.Info($"Gesichert nach {journal.BackupDirectory}.");

            foreach (var component in UpdaterLayout.Components)
                Swap(component, packageDirectory, journal);

            journal.NewVersionStarted = true;
            state.WriteJournal(journal);
            status.Report(UpdatePhase.Installing, $"Starte {status.TargetVersion}.");
            service.Start(timings.ServiceTimeout);

            if (!await WaitForVersionAsync(target, ct))
                throw new UpdateException(
                    $"Version {status.TargetVersion} hat sich nicht innerhalb von {timings.HealthTimeout.TotalSeconds:0} s unter /api/version gemeldet.");

            state.DeleteJournal();
            status.Report(UpdatePhase.Succeeded,
                $"{status.TargetVersion} läuft. Sicherung: {Path.GetFileName(journal.BackupDirectory)}.");
            return 0;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.Error($"Installation gescheitert: {ex}");
            var reason = ex is UpdateException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}";
            return await RollbackAsync(journal, canReplaceUpdater: true, reason, status, ct) ? 1 : 2;
        }
    }

    // Rollt nach einem abgebrochenen apply zurück (Journal lag noch da). Läuft aus updater\, kann
    // also updater\ selbst nicht zurücktauschen; das neue updater\ bleibt dann stehen.
    public Task<bool> RecoverAsync(UpdateJournal journal, CancellationToken ct)
    {
        var status = new StatusReporter(state, time, log)
        {
            FromVersion = journal.FromVersion,
            TargetVersion = journal.TargetVersion,
        };
        return RollbackAsync(journal, canReplaceUpdater: false,
            $"Das Update auf {journal.TargetVersion} wurde unterbrochen.", status, ct);
    }

    private async Task<bool> RollbackAsync(UpdateJournal journal, bool canReplaceUpdater, string reason,
        StatusReporter status, CancellationToken ct)
    {
        status.Report(UpdatePhase.Installing, $"{reason} Rolle zurück auf {journal.FromVersion ?? "den alten Stand"}.");
        try
        {
            service.Stop(timings.ServiceTimeout);

            for (var i = journal.Swapped.Count - 1; i >= 0; i--)
            {
                var component = journal.Swapped[i];
                if (component.Name == UpdaterLayout.UpdaterComponent && !canReplaceUpdater)
                {
                    log.Warn("updater\\ bleibt auf dem neuen Stand (läuft gerade selbst).");
                    continue;
                }
                RestoreComponent(component);
            }

            if (journal.NewVersionStarted && journal.BackupDirectory is not null)
            {
                RestoreData(journal.BackupDirectory);
                log.Info($"data\\ und keys\\ aus {journal.BackupDirectory} zurückgespielt.");
            }

            if (journal.ServiceStopped)
            {
                service.Start(timings.ServiceTimeout);
                if (UpdatePackage.TryParseTag("v" + journal.FromVersion, out var from) && !await WaitForVersionAsync(from, ct))
                    throw new UpdateException($"Die alte Version {journal.FromVersion} meldet sich nicht unter /api/version.");
            }

            state.DeleteJournal();
            status.Report(UpdatePhase.RolledBack, $"{reason} Der alte Stand ({journal.FromVersion ?? "?"}) läuft wieder.");
            return true;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.Error($"Zurückrollen gescheitert: {ex}");
            // Journal löschen: Ein neuer Lauf würde sonst bei jedem Start erneut zurückrollen und
            // dabei womöglich mehr zerstören. Ab hier ist Handarbeit nötig.
            state.DeleteJournal();
            status.Report(UpdatePhase.RollbackFailed,
                $"{reason} Auch das Zurückrollen ist gescheitert ({ex.Message}). Handarbeit nötig: " +
                $"Dienst stoppen, *.previous-Ordner nach app\\, tools\\, updater\\ zurück, " +
                $"data\\ und keys\\ aus {journal.BackupDirectory ?? "backups\\"} zurückspielen, Dienst starten. " +
                "Log unter update\\logs\\.");
            return false;
        }
    }

    private string Backup(Version target)
    {
        var stamp = time.GetUtcNow().UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var backup = Path.Combine(layout.Backups, $"pre-{UpdatePackage.Format(target)}-{stamp}");
        if (Directory.Exists(backup)) _files.DeleteDirectory(backup);
        Directory.CreateDirectory(backup);

        // Der Dienst ist gestoppt, die WAL also eingespielt; -wal/-shm kommen trotzdem mit.
        if (Directory.Exists(layout.Data)) _files.CopyDirectory(layout.Data, Path.Combine(backup, "data"));
        if (Directory.Exists(layout.Keys)) _files.CopyDirectory(layout.Keys, Path.Combine(backup, "keys"));

        PruneBackups(keepPath: backup);
        return backup;
    }

    private void PruneBackups(string keepPath)
    {
        var old = new DirectoryInfo(layout.Backups).GetDirectories("pre-*")
            .Where(d => !string.Equals(d.FullName, keepPath, StringComparison.OrdinalIgnoreCase))
            // Nach dem Zeitstempel im Namen (pre-X.Y.Z-yyyyMMdd-HHmmss), nicht nach Dateisystem-Zeiten.
            .OrderByDescending(d => d.Name.Length >= 15 ? d.Name[^15..] : "", StringComparer.Ordinal)
            .Skip(KeepBackups - 1);
        foreach (var dir in old)
        {
            log.Info($"Alte Sicherung entfernt: {dir.Name}");
            _files.DeleteDirectory(dir.FullName);
        }
    }

    private void RestoreData(string backup)
    {
        foreach (var (name, target) in new[] { ("data", layout.Data), ("keys", layout.Keys) })
        {
            var source = Path.Combine(backup, name);
            if (!Directory.Exists(source)) continue;
            // In den bestehenden Ordner zurückkopieren, damit seine Rechte bleiben.
            Directory.CreateDirectory(target);
            _files.ClearDirectory(target);
            _files.CopyDirectory(source, target);
        }
    }

    private void Swap(string name, string packageDirectory, UpdateJournal journal)
    {
        var target = layout.Component(name);
        var previous = layout.Previous(name);
        var source = Path.Combine(packageDirectory, name);

        // Den Stand vor dem vorigen Update braucht niemand mehr.
        _files.DeleteDirectory(previous);

        journal.Swapped.Add(new SwappedComponent(name, Directory.Exists(target)));
        state.WriteJournal(journal);

        if (Directory.Exists(target)) _files.MoveDirectory(target, previous);

        // updater\ wird kopiert, nicht verschoben: Aus diesem Ordner läuft apply gerade selbst.
        if (name == UpdaterLayout.UpdaterComponent)
            _files.CopyDirectory(source, target);
        else
            _files.MoveDirectory(source, target);

        permissions.ResetToInherited(target);
        log.Info($"{name}\\ getauscht.");
    }

    private void RestoreComponent(SwappedComponent component)
    {
        var target = layout.Component(component.Name);
        var previous = layout.Previous(component.Name);

        if (component.HadPrevious)
        {
            // Fehlt *.previous, kam der Tausch nicht so weit: Der alte Stand liegt noch in target.
            if (!Directory.Exists(previous)) return;
            _files.DeleteDirectory(target);
            _files.MoveDirectory(previous, target);
        }
        else
        {
            _files.DeleteDirectory(target);
        }
        log.Info($"{component.Name}\\ zurückgetauscht.");
    }

    private async Task<bool> WaitForVersionAsync(Version expected, CancellationToken ct)
    {
        var baseUri = service.GetLocalBaseUri();
        var deadline = time.GetUtcNow() + timings.HealthTimeout;
        while (true)
        {
            var reported = await health.GetVersionAsync(baseUri, ct);
            if (UpdatePackage.TryParseInformational(reported, out var version) && version == expected)
                return true;
            if (time.GetUtcNow() >= deadline) return false;
            await Task.Delay(timings.PollInterval, time, ct);
        }
    }

    private void TryStartService()
    {
        try
        {
            service.Start(timings.ServiceTimeout);
        }
        catch (Exception ex)
        {
            log.Warn($"Dienst ließ sich nicht wieder starten: {ex.Message}");
        }
    }
}
