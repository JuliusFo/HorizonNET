using System.Net;
using System.Text.Json;
using HorizonNET.Shared.Transfer.Update;
using Microsoft.Extensions.Options;

namespace HorizonNET.Api.Services.Update;

// Fragt das neueste GitHub-Release ab (ohne Token, öffentliches Repo) und merkt sich, ob es
// neuer ist als die laufende Version. Beim Start nach InitialDelay, dann alle Interval, und auf
// „Jetzt prüfen“. Fehler (offline, Rate-Limit) landen als Hinweis im Ergebnis, nie als Ausnahme.
public sealed class UpdateCheckService(
    IHttpClientFactory httpFactory,
    IOptions<UpdateOptions> options,
    AppVersionInfo appVersion,
    TimeProvider time,
    ILogger<UpdateCheckService> logger)
{
    public const string HttpClientName = "github-update";

    // Die Antwort kommt von außen: Größe begrenzen, Notes kürzen.
    public const int MaxResponseBytes = 2 * 1024 * 1024;
    private const int MaxNotesChars = 20_000;

    // „Jetzt prüfen“ höchstens alle 30 s; GitHub erlaubt ohne Token 60 Anfragen je Stunde.
    private static readonly TimeSpan ManualThrottle = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile UpdateCheckResult _last = UpdateCheckResult.Never;

    public UpdateCheckResult Last => _last;

    public async Task<UpdateCheckResult> CheckAsync(bool manual, CancellationToken ct)
    {
        if (options.Value.Mode == UpdateMode.Off) return _last;

        await _gate.WaitAsync(ct);
        try
        {
            var now = time.GetUtcNow().UtcDateTime;
            if (manual && _last.CheckedUtc is { } previous && now - previous < ManualThrottle)
                return _last;

            _last = await QueryAsync(now, ct);
            return _last;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<UpdateCheckResult> QueryAsync(DateTime now, CancellationToken ct)
    {
        try
        {
            var http = httpFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, UpdatePackage.LatestReleaseApi);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            request.Headers.UserAgent.ParseAdd(UpdatePackage.UserAgent);

            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new UpdateCheckResult(now, null, null);
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                return new UpdateCheckResult(now, null, "GitHub begrenzt gerade die Anfragen; später erneut prüfen.");
            if (!response.IsSuccessStatusCode)
                return new UpdateCheckResult(now, null, $"GitHub antwortet mit {(int)response.StatusCode}.");
            if (response.Content.Headers.ContentLength > MaxResponseBytes)
                return new UpdateCheckResult(now, null, "Antwort von GitHub ist zu groß.");

            var bytes = await ReadLimitedAsync(response, ct);
            if (bytes is null) return new UpdateCheckResult(now, null, "Antwort von GitHub ist zu groß.");

            var release = JsonSerializer.Deserialize<GitHubRelease>(bytes);
            var candidate = UpdatePackage.Evaluate(release);
            if (candidate is null || appVersion.Version is null || candidate.Version <= appVersion.Version)
                return new UpdateCheckResult(now, null, null);

            var notes = candidate.Notes.Length > MaxNotesChars ? candidate.Notes[..MaxNotesChars] + " …" : candidate.Notes;
            var dto = new UpdateReleaseDto(UpdatePackage.Format(candidate.Version), notes, candidate.PublishedAt,
                SafeReleaseUrl(candidate.HtmlUrl));
            logger.LogInformation("Update verfügbar: {Version}", dto.Version);
            return new UpdateCheckResult(now, dto, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
                                       || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning("Update-Prüfung fehlgeschlagen: {Message}", ex.Message);
            return new UpdateCheckResult(now, null, "GitHub ist nicht erreichbar.");
        }
    }

    private static async Task<byte[]?> ReadLimitedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    // Der Link geht in den Client: nur https auf Releases des eigenen Repos.
    public static string? SafeReleaseUrl(string? url) =>
        url is not null
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host == "github.com"
        && uri.AbsolutePath.StartsWith($"/{UpdatePackage.Repository}/releases/", StringComparison.Ordinal)
            ? uri.AbsoluteUri
            : null;
}

public sealed record UpdateCheckResult(DateTime? CheckedUtc, UpdateReleaseDto? Available, string? Error)
{
    public static UpdateCheckResult Never { get; } = new(null, null, null);
}

// Stößt die Prüfung beim Start und danach regelmäßig an.
public sealed class UpdateCheckHost(UpdateCheckService checker, IOptions<UpdateOptions> options, TimeProvider time)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.Mode == UpdateMode.Off) return;
        try
        {
            await Task.Delay(options.Value.InitialDelay, time, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await checker.CheckAsync(manual: false, stoppingToken);
                await Task.Delay(options.Value.Interval, time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Herunterfahren
        }
    }
}
