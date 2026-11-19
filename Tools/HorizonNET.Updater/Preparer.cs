using HorizonNET.Shared.Transfer.Update;

namespace HorizonNET.Updater;

// run / install: läuft aus dem installierten updater\.
//   1. Liegt ein Journal (apply wurde unterbrochen), zuerst zurückrollen. Scheitert das, endet der
//      Lauf (Exit 2): Auf einem halb zurückgerollten Stand wird nichts installiert.
//   2. run (Aufgabe „HorizonNET Recover“ beim Systemstart) endet hier.
//   3. install (Aufgabe „HorizonNET Update“, vom Dienstkonto gestartet, oder von Hand):
//      neuestes Release mit Paket holen, ZIP und Prüfsumme laden, prüfen, entpacken.
//   4. apply aus dem neuen Paket starten und sich beenden.
internal sealed class Preparer(
    UpdaterLayout layout,
    StateFiles state,
    IReleaseSource source,
    IInstalledVersion installed,
    IApplyLauncher launcher,
    Installer installer,
    UpdaterTimings timings,
    TimeProvider time,
    UpdateLog log)
{
    public async Task<int> RunAsync(bool install, CancellationToken ct)
    {
        layout.EnsureWorkDirectories();

        var updateLock = await UpdateLock.TryAcquireAsync(layout.LockFile, TimeSpan.Zero, time);
        if (updateLock is null)
        {
            log.Info("Ein anderer Updater-Lauf hält die Sperre – nichts zu tun.");
            return 0;
        }

        try
        {
            var journal = state.ReadJournal();
            if (journal is not null)
            {
                log.Warn($"Unterbrochenes Update auf {journal.TargetVersion} gefunden – rolle zurück.");
                if (!await installer.RecoverAsync(journal, ct)) return 2;
            }

            if (!install)
            {
                log.Info("run: fertig (installiert wird nur mit install).");
                return 0;
            }

            return await PrepareAsync(updateLock, ct);
        }
        finally
        {
            updateLock.Dispose();
        }
    }

    private async Task<int> PrepareAsync(UpdateLock updateLock, CancellationToken ct)
    {
        var status = new StatusReporter(state, time, log);
        var from = installed.Read(layout);
        status.FromVersion = from is null ? null : UpdatePackage.Format(from);
        status.Report(UpdatePhase.Preparing, "Suche das neueste Release.");

        var files = new FileOps(timings);
        string? staging = null;
        try
        {
            // Reste früherer Läufe; apply kann seinen eigenen staging-Ordner nicht löschen.
            files.ClearDirectory(layout.Staging);
            files.ClearDirectory(layout.Downloads);

            var release = await source.GetLatestAsync(ct);
            if (release is null)
            {
                status.Report(UpdatePhase.NoUpdate, "Kein Release mit Update-Paket gefunden.");
                return 0;
            }

            status.TargetVersion = UpdatePackage.Format(release.Version);
            if (from is not null && release.Version <= from)
            {
                status.Report(UpdatePhase.NoUpdate, $"{status.FromVersion} ist aktuell.");
                return 0;
            }

            status.Report(UpdatePhase.Downloading, $"Lade {status.TargetVersion}.");
            var zipName = UpdatePackage.ZipName(release.Version);
            var zip = Path.Combine(layout.Downloads, zipName);
            var checksum = Path.Combine(layout.Downloads, UpdatePackage.ChecksumName(release.Version));
            await source.DownloadAsync(release, zip, checksum, ct);
            PackageFiles.VerifyChecksum(zip, checksum, zipName);

            staging = layout.StagingFor(release.Version);
            PackageFiles.Extract(zip, staging);
            PackageFiles.Validate(staging, release.Version);
            files.ClearDirectory(layout.Downloads);

            status.Report(UpdatePhase.Installing, $"Paket {status.TargetVersion} geprüft, übergebe an die neue Version.");

            // Sperre freigeben, bevor apply startet; apply wartet auf das Ende dieses Prozesses.
            updateLock.Dispose();
            launcher.StartApply(
                Path.Combine(staging, UpdaterLayout.UpdaterComponent, UpdaterLayout.UpdaterExe),
                layout.Root, staging, Environment.ProcessId);
            return 0;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.Error($"Vorbereitung gescheitert: {ex}");
            var message = ex switch
            {
                UpdateException => ex.Message,
                HttpRequestException http => $"GitHub nicht erreichbar oder Fehler beim Download ({http.Message}).",
                TaskCanceledException => "Zeitüberschreitung bei GitHub.",
                _ => $"{ex.GetType().Name}: {ex.Message}",
            };
            if (staging is not null)
            {
                try { files.DeleteDirectory(staging); }
                catch (Exception cleanup) { log.Warn($"Aufräumen gescheitert: {cleanup.Message}"); }
            }
            status.Report(UpdatePhase.Failed, $"{message} Nichts geändert.");
            return 1;
        }
    }
}
