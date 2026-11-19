using System.Globalization;
using HorizonNET.Shared.Transfer.Update;

namespace HorizonNET.Updater;

// apply: läuft aus dem NEUEN Paket (update\staging\X.Y.Z\updater\), damit die Installationslogik
// der neuen Version gilt und updater\ ersetzt werden kann. Schritte (Journal vor jedem):
//   Starttyp merken und auf „Deaktiviert“ → Dienst stoppen → DB und Schlüsselring sichern →
//   app\, tools\, updater\ tauschen (alter Stand nach *.previous) → Starttyp zurück, Dienst
//   starten → /api/version == neue Version?
// Scheitert etwas nach dem Stoppen, rollt es zurück: Ordner aus *.previous, und wenn die neue
// Version schon gestartet sein kann (Migrationen!), auch DB und Schlüsselring aus der Sicherung.
//
// data\ und keys\ kann das Dienstkonto beschreiben, der Updater läuft als SYSTEM: Gesichert und
// zurückgespielt werden nur bekannte Dateien, flach und ohne Verweisen (Reparse Points) zu folgen,
// und zurückgespielt wird in einen neuen Ordner, den nur SYSTEM beschreiben kann (Review 2026-11-19, S2).
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

    // ConnectionStrings__DefaultConnection aus server-install.ps1: data\horizonnet.db im WAL-Modus.
    internal static readonly string[] DataFiles = ["horizonnet.db", "horizonnet.db-wal", "horizonnet.db-shm"];

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
            // backups\ trägt eigene Rechte (nur SYSTEM und Admins); mit geerbten könnte das
            // Dienstkonto die Sicherungen samt Schlüsselring lesen.
            if (!Directory.Exists(layout.Backups))
                throw new UpdateException($"{layout.Backups} fehlt – server-install.ps1 ausführen.");
        }
        catch (UpdateException ex)
        {
            status.Report(UpdatePhase.Failed, ex.Message);
            return 1;
        }

        var from = installed.Read(layout);
        status.FromVersion = from is null ? null : UpdatePackage.Format(from);
        status.TargetVersion = UpdatePackage.Format(target);

        // Vor dem Journal: Hier ist noch nichts geändert, ein Abbruch lässt nichts zurück.
        ServiceStartType startType;
        try
        {
            startType = service.GetStartType();
        }
        catch (Exception ex)
        {
            status.Report(UpdatePhase.Failed, $"Der Starttyp des Dienstes ist nicht lesbar ({ex.Message}). Nichts geändert.");
            return 1;
        }
        if (startType == ServiceStartType.Disabled)
        {
            // Etwa nach einem gescheiterten Zurückrollen: Erst muss jemand nachsehen.
            status.Report(UpdatePhase.Failed,
                "Der Dienst ist deaktiviert (etwa nach einem gescheiterten Zurückrollen). Erst von Hand prüfen " +
                "und den Starttyp zurücksetzen. Nichts geändert.");
            return 1;
        }

        var journal = new UpdateJournal
        {
            TargetVersion = status.TargetVersion,
            FromVersion = status.FromVersion,
            StartedUtc = time.GetUtcNow().UtcDateTime,
            OriginalStartType = startType,
        };
        state.WriteJournal(journal);
        status.Report(UpdatePhase.Installing, $"Installiere {status.TargetVersion}: Dienst wird gestoppt.");

        try
        {
            service.SetStartType(ServiceStartType.Disabled);
            service.Stop(timings.ServiceTimeout);
        }
        catch (Exception ex)
        {
            // Noch nichts getauscht: Starttyp zurück, Dienst möglichst wieder an, Journal weg.
            var restored = TryRestoreStartType(journal);
            TryStartService();
            if (restored) state.DeleteJournal();
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
            service.SetStartType(startType);
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
            // Erst deaktivieren, dann stoppen: Stürzt die neue Version beim Start ab, startet der
            // Dienststeuerungs-Manager sie per Fehleraktion neu (server-install.ps1: restart/5000) –
            // auch mitten im Zurücktauschen. Nur ein Zusatzschutz: Scheitert er, wird trotzdem
            // zurückgerollt.
            if (journal.OriginalStartType is not null)
            {
                try
                {
                    service.SetStartType(ServiceStartType.Disabled);
                }
                catch (Exception ex)
                {
                    log.Warn($"Dienst ließ sich vor dem Zurückrollen nicht deaktivieren: {ex.Message}");
                }
            }
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
                log.Info($"DB und Schlüsselring aus {journal.BackupDirectory} zurückgespielt.");
            }

            if (journal.OriginalStartType is { } startType) service.SetStartType(startType);

            // Immer starten, auch wenn das Stoppen beim Update nicht mehr im Journal stand: Vor dem
            // Update lief der Dienst (oder apply hätte ihn am Ende ohnehin gestartet).
            service.Start(timings.ServiceTimeout);
            if (UpdatePackage.TryParseTag("v" + journal.FromVersion, out var from) && !await WaitForVersionAsync(from, ct))
                throw new UpdateException($"Die alte Version {journal.FromVersion} meldet sich nicht unter /api/version.");

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
                $"DB und Schlüsselring aus {journal.BackupDirectory ?? "backups\\"} zurückspielen, " +
                $"Starttyp des Dienstes auf {journal.OriginalStartType?.ToString() ?? "Automatic"} setzen, Dienst starten. " +
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
        CopyKnownFiles(layout.Data, Path.Combine(backup, "data"), IsDataFile);
        CopyKnownFiles(layout.Keys, Path.Combine(backup, "keys"), IsKeyFile);

        PruneBackups(keepPath: backup);
        return backup;
    }

    private static bool IsDataFile(string name) => DataFiles.Contains(name, StringComparer.OrdinalIgnoreCase);

    // DataProtection legt den Ring als key-<guid>.xml ab.
    private static bool IsKeyFile(string name) => string.Equals(Path.GetExtension(name), ".xml", StringComparison.OrdinalIgnoreCase);

    // Kopiert nur Dateien direkt in source, die include zulässt; Unterordner bleiben liegen. Ist
    // source selbst oder eine der Dateien ein Verweis, bricht es ab: Als SYSTEM einem Verweis zu
    // folgen, den das Dienstkonto gesetzt hat, hieße für es fremde Dateien zu lesen.
    private void CopyKnownFiles(string source, string destination, Func<string, bool> include)
    {
        if (!Directory.Exists(source)) return;
        RejectReparsePoint(source);
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            var name = Path.GetFileName(file);
            if (!include(name)) continue;
            RejectReparsePoint(file);
            _files.CopyFile(file, Path.Combine(destination, name));
        }
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new UpdateException($"{path} ist ein Verweis (Reparse Point) – abgebrochen.");
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

    // Nicht in den bestehenden Ordner schreiben: Den kann das Dienstkonto während der Rücksicherung
    // in einen Verweis verwandeln. Stattdessen <ordner>.restore anlegen (erbt von Root, also für das
    // Dienstkonto nur lesbar), füllen, die Rechte des alten Ordners übernehmen und tauschen. Der
    // alte Stand bleibt als <ordner>.failed-<zeit> liegen (DB der gescheiterten Version, zur Diagnose).
    //
    // .failed- und .restore-Ordner löscht der Updater nie: Sie tragen die Rechte des Dienstkontos,
    // und rekursives Löschen als SYSTEM ließe sich per Junction auf beliebige Ordner umlenken
    // (Review 2026-11-19, zweiter Durchgang). Eindeutige Namen statt Löschen; aufräumen von Hand.
    private void RestoreData(string backup)
    {
        var stamp = time.GetUtcNow().UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
                    + "-" + Guid.NewGuid().ToString("N")[..6];
        foreach (var (name, target, include) in new (string, string, Func<string, bool>)[]
                 {
                     ("data", layout.Data, IsDataFile),
                     ("keys", layout.Keys, IsKeyFile),
                 })
        {
            var source = Path.Combine(backup, name);
            if (!Directory.Exists(source)) continue;

            var restore = $"{target}.restore-{stamp}";
            var failed = $"{target}.failed-{stamp}";
            CopyKnownFiles(source, restore, include);

            if (Directory.Exists(target))
            {
                permissions.CopyAccess(target, restore);
                _files.MoveDirectory(target, failed);
                log.Warn($"Stand vor der Rücksicherung liegt in {Path.GetFileName(failed)}\\ – nach Prüfung von Hand löschen.");
            }
            else if (LatestFailed(target) is { } earlier)
            {
                // Absturz zwischen den beiden Umbenennungen eines früheren Laufs: Der alte Ordner liegt
                // schon als .failed-… da und trägt die richtigen Rechte.
                permissions.CopyAccess(earlier, restore);
                log.Warn($"{name}\\ fehlte; Rechte von {Path.GetFileName(earlier)}\\ übernommen.");
            }
            else
            {
                log.Warn($"{name}\\ fehlte; die Rücksicherung trägt die Rechte von {layout.Root}. Rechte prüfen.");
            }
            _files.MoveDirectory(restore, target);
        }
    }

    // Jüngster <ordner>.failed-yyyyMMdd-HHmmss-… (der Zeitstempel im Namen sortiert).
    private static string? LatestFailed(string target)
    {
        var parent = Path.GetDirectoryName(target)!;
        var prefix = Path.GetFileName(target) + ".failed-";
        return Directory.GetDirectories(parent, prefix + "*")
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private void Swap(string name, string packageDirectory, UpdateJournal journal)
    {
        var target = layout.Component(name);
        var previous = layout.Previous(name);
        var source = Path.Combine(packageDirectory, name);

        // updater\ wird kopiert, nicht verschoben: Aus dem Paket läuft apply gerade selbst. Erst in
        // updater.new, dann umbenennen – bricht das Kopieren ab, bleibt updater\ heil, und die
        // Aufgabe „HorizonNET Recover“ findet beim nächsten Start ihr Programm.
        if (name == UpdaterLayout.UpdaterComponent)
        {
            var copy = target + ".new";
            _files.DeleteDirectory(copy);
            _files.CopyDirectory(source, copy);
            source = copy;
        }

        // Den Stand vor dem vorigen Update braucht niemand mehr.
        _files.DeleteDirectory(previous);

        journal.Swapped.Add(new SwappedComponent(name, Directory.Exists(target)));
        state.WriteJournal(journal);

        if (Directory.Exists(target)) _files.MoveDirectory(target, previous);
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

    // false: Der Starttyp steht womöglich noch auf „Deaktiviert“ – dann bleibt das Journal liegen,
    // und der nächste Lauf (spätestens „HorizonNET Recover“ beim Systemstart) setzt ihn zurück.
    private bool TryRestoreStartType(UpdateJournal journal)
    {
        if (journal.OriginalStartType is not { } startType) return true;
        try
        {
            service.SetStartType(startType);
            return true;
        }
        catch (Exception ex)
        {
            log.Warn($"Starttyp des Dienstes ließ sich nicht zurücksetzen: {ex.Message}");
            return false;
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
