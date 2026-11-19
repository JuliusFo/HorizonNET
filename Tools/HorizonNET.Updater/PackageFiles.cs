using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using HorizonNET.Shared.Transfer.Update;

namespace HorizonNET.Updater;

// Prüfsumme, Entpacken und Inhaltsprüfung des Pakets.
internal static class PackageFiles
{
    // Das Paket ist etwa 150 MB groß, entpackt etwa 350 MB. Großzügige Obergrenzen gegen
    // einen kaputten oder aufgeblähten Download.
    public const long MaxZipBytes = 1L << 30;
    private const long MaxExtractedBytes = 3L << 30;

    public static void VerifyChecksum(string zipPath, string checksumPath, string expectedZipName)
    {
        // Format wie sha256sum: "<hash>  <datei>"
        var parts = File.ReadAllText(checksumPath).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts[0].Length != 64 || !parts[0].All(Uri.IsHexDigit))
            throw new UpdateException("Die Prüfsummendatei hat kein gültiges Format.");
        if (parts.Length > 1 && parts[1].TrimStart('*') != expectedZipName)
            throw new UpdateException($"Die Prüfsummendatei gehört zu {parts[1]}, nicht zu {expectedZipName}.");

        using var stream = File.OpenRead(zipPath);
        var actual = Convert.ToHexStringLower(SHA256.HashData(stream));
        if (!string.Equals(actual, parts[0], StringComparison.OrdinalIgnoreCase))
            throw new UpdateException("Die Prüfsumme des Pakets stimmt nicht – Download beschädigt.");
    }

    public static void Extract(string zipPath, string destination)
    {
        if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);

        using (var archive = ZipFile.OpenRead(zipPath))
        {
            long total = 0;
            foreach (var entry in archive.Entries)
            {
                total += entry.Length;
                if (total > MaxExtractedBytes) throw new UpdateException("Das Paket ist entpackt zu groß.");
            }
        }

        // ExtractToDirectory lehnt Einträge ab, die aus dem Zielordner herausführen ("..\").
        ZipFile.ExtractToDirectory(zipPath, destination);
    }

    // Prüft manifest.json und die Programme im entpackten Paket und gibt die Version zurück.
    public static Version Validate(string packageDirectory, Version? expected)
    {
        var manifestPath = Path.Combine(packageDirectory, "manifest.json");
        if (!File.Exists(manifestPath)) throw new UpdateException("Im Paket fehlt manifest.json.");

        PackageManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize(File.ReadAllBytes(manifestPath), UpdaterJson.Default.PackageManifest);
        }
        catch (JsonException)
        {
            throw new UpdateException("manifest.json im Paket ist kein gültiges JSON.");
        }

        if (manifest?.Product != "HorizonNET") throw new UpdateException("Das Paket ist kein HorizonNET-Paket.");
        if (manifest.RuntimeId != UpdatePackage.RuntimeId)
            throw new UpdateException($"Das Paket ist für {manifest.RuntimeId}, nicht für {UpdatePackage.RuntimeId}.");
        if (!UpdatePackage.TryParseInformational(manifest.Version, out var version))
            throw new UpdateException($"Version im Paket ungültig: {manifest.Version}");
        if (expected is not null && version != expected)
            throw new UpdateException($"Das Paket enthält {UpdatePackage.Format(version)} statt {UpdatePackage.Format(expected)}.");

        foreach (var required in new[]
                 {
                     Path.Combine(UpdaterLayout.AppComponent, UpdaterLayout.ApiExe),
                     Path.Combine(UpdaterLayout.ToolsComponent, UpdaterLayout.ToolExe),
                     Path.Combine(UpdaterLayout.UpdaterComponent, UpdaterLayout.UpdaterExe),
                 })
        {
            if (!File.Exists(Path.Combine(packageDirectory, required)))
                throw new UpdateException($"Im Paket fehlt {required}.");
        }

        return version;
    }
}

// Datei-Operationen mit Wiederholung: Nach dem Stoppen hält der Dienstprozess seine Dateien
// manchmal noch einen Moment, und Virenscanner öffnen frisch entpackte Dateien.
internal sealed class FileOps(UpdaterTimings timings)
{
    public void MoveDirectory(string source, string destination) =>
        Retry(() => Directory.Move(source, destination));

    public void DeleteDirectory(string path) =>
        Retry(() =>
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        });

    public void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Retry(() => File.Copy(file, target, overwrite: true));
        }
    }

    // Leert einen Ordner, ohne ihn selbst zu löschen: Seine Rechte (keys\!) bleiben so erhalten.
    public void ClearDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        foreach (var file in Directory.GetFiles(path))
            Retry(() => File.Delete(file));
        foreach (var dir in Directory.GetDirectories(path))
            DeleteDirectory(dir);
    }

    private void Retry(Action action)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < timings.IoRetries)
            {
                Thread.Sleep(timings.IoRetryDelay);
            }
        }
    }
}
