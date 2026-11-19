using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using HorizonNET.Shared.Transfer.Update;

namespace HorizonNET.Updater;

// Das neueste Release des fest eingebauten Repos, ohne Token (öffentliches Repo, 60 Anfragen je
// Stunde reichen weit). Download-Adressen baut UpdatePackage aus der Version; Links aus der
// Antwort werden nicht benutzt.
// api: kurzes Zeitlimit (Release-Abfrage, Prüfsumme), download: langes Zeitlimit (ZIP).
internal sealed class GitHubReleaseSource(HttpClient api, HttpClient download) : IReleaseSource
{
    private const long MaxApiBytes = 2 * 1024 * 1024;
    private const long MaxChecksumBytes = 4 * 1024;

    public async Task<ReleaseCandidate?> GetLatestAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UpdatePackage.LatestReleaseApi);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var response = await api.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null; // noch kein Release
        response.EnsureSuccessStatusCode();

        using var buffer = new MemoryStream();
        await CopyLimitedAsync(response, buffer, MaxApiBytes, ct);
        var release = JsonSerializer.Deserialize(buffer.ToArray(), UpdaterJson.Default.GitHubRelease);
        return UpdatePackage.Evaluate(release);
    }

    public async Task DownloadAsync(ReleaseCandidate release, string zipPath, string checksumPath, CancellationToken ct)
    {
        await DownloadFileAsync(api, UpdatePackage.DownloadUri(release.Version, UpdatePackage.ChecksumName(release.Version)),
            checksumPath, MaxChecksumBytes, ct);
        await DownloadFileAsync(download, UpdatePackage.DownloadUri(release.Version, UpdatePackage.ZipName(release.Version)),
            zipPath, PackageFiles.MaxZipBytes, ct);
    }

    private static async Task DownloadFileAsync(HttpClient http, Uri uri, string path, long maxBytes, CancellationToken ct)
    {
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await CopyLimitedAsync(response, file, maxBytes, ct);
    }

    private static async Task CopyLimitedAsync(HttpResponseMessage response, Stream target, long maxBytes, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > maxBytes)
            throw new UpdateException($"Antwort von {response.RequestMessage?.RequestUri?.Host} ist zu groß.");

        await using var source = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > maxBytes)
                throw new UpdateException($"Antwort von {response.RequestMessage?.RequestUri?.Host} ist zu groß.");
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }
}

// --source <ordner>: Paket aus einem lokalen Ordner statt von GitHub (Tests, Notfall, Probe auf
// dem Server). Nur über die Kommandozeile `install`, nie über die Anfrage der API.
internal sealed partial class LocalReleaseSource(string directory) : IReleaseSource
{
    public Task<ReleaseCandidate?> GetLatestAsync(CancellationToken ct)
    {
        ReleaseCandidate? best = null;
        foreach (var file in Directory.GetFiles(directory, "HorizonNET-*-win-x64.zip"))
        {
            var match = ZipNamePattern().Match(Path.GetFileName(file));
            if (!match.Success || !UpdatePackage.TryParseTag("v" + match.Groups[1].Value, out var version)) continue;
            if (!File.Exists(file + ".sha256")) continue;
            if (best is null || version > best.Version)
                best = new ReleaseCandidate(version, "v" + UpdatePackage.Format(version), new FileInfo(file).Length, "", null, null);
        }
        return Task.FromResult(best);
    }

    public Task DownloadAsync(ReleaseCandidate release, string zipPath, string checksumPath, CancellationToken ct)
    {
        var zip = Path.Combine(directory, UpdatePackage.ZipName(release.Version));
        File.Copy(zip, zipPath, overwrite: true);
        File.Copy(zip + ".sha256", checksumPath, overwrite: true);
        return Task.CompletedTask;
    }

    [GeneratedRegex(@"^HorizonNET-(\d+\.\d+\.\d+)-win-x64\.zip$")]
    private static partial Regex ZipNamePattern();
}
