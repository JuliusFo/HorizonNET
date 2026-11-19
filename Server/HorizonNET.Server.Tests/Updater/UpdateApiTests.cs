using System.Net;
using System.Text;
using System.Text.Json;
using HorizonNET.Api.Services.Update;
using HorizonNET.Shared.Transfer.Update;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HorizonNET.Server.Tests.Updater;

// UpdateCheckService und UpdateCoordinator ohne Netz: GitHub antwortet über einen Handler,
// die geplante Aufgabe ist eine Attrappe, update\ ein Temp-Ordner.
public sealed class UpdateApiTests : IDisposable
{
    private readonly string _updateDir = Path.Combine(Path.GetTempPath(), "hn-update-api-" + Guid.NewGuid().ToString("N"));
    private readonly FakeHandler _github = new();
    private readonly FakeStarter _starter = new();
    private readonly UpdateOptions _options;

    public UpdateApiTests()
    {
        Directory.CreateDirectory(_updateDir);
        _options = new UpdateOptions { Mode = UpdateMode.Install, Directory = _updateDir };
    }

    public void Dispose()
    {
        try { Directory.Delete(_updateDir, recursive: true); }
        catch (IOException) { /* Temp-Ordner */ }
    }

    [Fact]
    public async Task Check_NewerCompleteRelease_IsAvailable()
    {
        _github.Respond(HttpStatusCode.OK, ReleaseJson("v1.4.0", "https://github.com/JuliusFo/HorizonNET/releases/tag/v1.4.0"));

        var result = await Checker().CheckAsync(manual: false, CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal("1.4.0", result.Available?.Version);
        Assert.Equal("### Neu\n- Etwas", result.Available?.Notes);
        Assert.Equal("https://github.com/JuliusFo/HorizonNET/releases/tag/v1.4.0", result.Available?.Url);
        Assert.Equal("HorizonNET-Update", _github.LastRequest?.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task Check_SameOrOlderVersion_IsNoUpdate()
    {
        _github.Respond(HttpStatusCode.OK, ReleaseJson("v1.3.0"));

        var result = await Checker().CheckAsync(false, CancellationToken.None);

        Assert.Null(result.Available);
        Assert.Null(result.Error);
        Assert.NotNull(result.CheckedUtc);
    }

    [Fact]
    public async Task Check_RateLimited_IsHintNotException()
    {
        _github.Respond(HttpStatusCode.Forbidden, "{}");

        var result = await Checker().CheckAsync(false, CancellationToken.None);

        Assert.Null(result.Available);
        Assert.Contains("begrenzt", result.Error);
    }

    // M2 (Review 2026-11-19): Bricht die Verbindung beim Lesen des Bodys ab, kommt eine
    // IOException (HttpIOException) statt HttpRequestException. Sie darf weder den manuellen Check
    // (500) noch den Hintergrunddienst (beendet den Host) erreichen.
    [Fact]
    public async Task Check_ConnectionDropsWhileReadingBody_IsHintNotException()
    {
        _github.RespondWithBrokenBody();

        var result = await Checker().CheckAsync(manual: false, CancellationToken.None);

        Assert.Null(result.Available);
        Assert.Contains("nicht erreichbar", result.Error);
    }

    [Fact]
    public async Task CheckHost_SurvivesUnexpectedErrorsAndKeepsChecking()
    {
        _options.InitialDelay = TimeSpan.Zero;
        _options.Interval = TimeSpan.FromMilliseconds(10);
        _github.Throw = new InvalidOperationException("unerwartet");
        using var host = new UpdateCheckHost(Checker(), Options.Create(_options), TimeProvider.System,
            NullLogger<UpdateCheckHost>.Instance);

        await host.StartAsync(CancellationToken.None);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (_github.Calls < 2 && DateTime.UtcNow < deadline) await Task.Delay(10);
        await host.StopAsync(CancellationToken.None);

        Assert.True(_github.Calls >= 2);
        Assert.False(host.ExecuteTask?.IsFaulted);
    }

    [Fact]
    public async Task Check_ModeOff_DoesNotAskGitHub()
    {
        _options.Mode = UpdateMode.Off;

        await Checker().CheckAsync(true, CancellationToken.None);

        Assert.Equal(0, _github.Calls);
    }

    [Fact]
    public async Task Check_ManualTwiceInARow_AsksGitHubOnce()
    {
        _github.Respond(HttpStatusCode.OK, ReleaseJson("v1.4.0"));
        var checker = Checker();

        await checker.CheckAsync(true, CancellationToken.None);
        await checker.CheckAsync(true, CancellationToken.None);

        Assert.Equal(1, _github.Calls);
    }

    [Theory]
    [InlineData("https://github.com/JuliusFo/HorizonNET/releases/tag/v1.4.0", true)]
    [InlineData("http://github.com/JuliusFo/HorizonNET/releases/tag/v1.4.0", false)]
    [InlineData("https://github.com/someone/else/releases/tag/v1.4.0", false)]
    [InlineData("https://evil.example/JuliusFo/HorizonNET/releases/", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData(null, false)]
    public void SafeReleaseUrl_OnlyOwnRepoOverHttps(string? url, bool kept) =>
        Assert.Equal(kept, UpdateCheckService.SafeReleaseUrl(url) is not null);

    [Fact]
    // S1 (Review 2026-11-19): Die API schreibt nichts, was der Updater (SYSTEM) liest; der Start
    // der Aufgabe ist der ganze Auftrag.
    public async Task Install_WithAvailableUpdate_OnlyStartsTask()
    {
        _github.Respond(HttpStatusCode.OK, ReleaseJson("v1.4.0"));
        var checker = Checker();
        await checker.CheckAsync(false, CancellationToken.None);
        var coordinator = Coordinator(checker);
        Assert.True(coordinator.GetInfo().CanInstall);

        var result = await coordinator.RequestInstallAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("HorizonNET Update", _starter.Started);
        Assert.Empty(Directory.GetFileSystemEntries(_updateDir));
    }

    [Fact]
    public async Task Install_InNotifyMode_IsRejected()
    {
        _github.Respond(HttpStatusCode.OK, ReleaseJson("v1.4.0"));
        var checker = Checker();
        await checker.CheckAsync(false, CancellationToken.None);
        _options.Mode = UpdateMode.Notify;

        var result = await Coordinator(checker).RequestInstallAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.IsServerError);
        Assert.Null(_starter.Started);
    }

    [Fact]
    public async Task Install_WithoutAvailableUpdate_IsRejected()
    {
        var result = await Coordinator(Checker()).RequestInstallAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(_starter.Started);
    }

    [Fact]
    public async Task Install_WhileUpdaterRuns_IsRejected_UnlessStatusIsStale()
    {
        _github.Respond(HttpStatusCode.OK, ReleaseJson("v1.4.0"));
        var checker = Checker();
        await checker.CheckAsync(false, CancellationToken.None);
        var coordinator = Coordinator(checker);

        WriteStatus(UpdatePhase.Downloading, DateTime.UtcNow.AddMinutes(-1));
        Assert.True(coordinator.GetInfo().IsInstalling);
        Assert.False((await coordinator.RequestInstallAsync(CancellationToken.None)).Success);

        WriteStatus(UpdatePhase.Downloading, DateTime.UtcNow.AddHours(-2));
        Assert.False(coordinator.GetInfo().IsInstalling);
        Assert.True((await coordinator.RequestInstallAsync(CancellationToken.None)).Success);
    }

    [Fact]
    public async Task Install_TaskDoesNotStart_IsServerError()
    {
        _github.Respond(HttpStatusCode.OK, ReleaseJson("v1.4.0"));
        var checker = Checker();
        await checker.CheckAsync(false, CancellationToken.None);
        _starter.Fail = true;

        var result = await Coordinator(checker).RequestInstallAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.IsServerError);
        Assert.Contains("Zugriff verweigert", result.Message);
    }

    [Fact]
    public void GetInfo_ReadsStatusWrittenByUpdater()
    {
        // So schreibt der Updater status.json (camelCase, Phase als Text).
        File.WriteAllText(Path.Combine(_updateDir, "status.json"), """
            { "phase": "RolledBack", "fromVersion": "1.3.0", "targetVersion": "1.4.0",
              "message": "zurückgerollt", "startedUtc": "2026-11-19T10:00:00Z",
              "updatedUtc": "2026-11-19T10:02:00Z", "finishedUtc": "2026-11-19T10:02:00Z" }
            """);

        var info = Coordinator(Checker()).GetInfo();

        Assert.Equal(UpdatePhase.RolledBack, info.LastRun?.Phase);
        Assert.Equal("1.4.0", info.LastRun?.TargetVersion);
        Assert.False(info.IsInstalling);
    }

    private UpdateCheckService Checker() =>
        new(new FakeFactory(_github), Options.Create(_options), Version("1.3.0"), TimeProvider.System,
            NullLogger<UpdateCheckService>.Instance);

    private UpdateCoordinator Coordinator(UpdateCheckService checker) =>
        new(checker, _starter, Options.Create(_options), Version("1.3.0"), TimeProvider.System,
            NullLogger<UpdateCoordinator>.Instance);

    private static AppVersionInfo Version(string version) => new(version + "+abc", System.Version.Parse(version));

    private void WriteStatus(UpdatePhase phase, DateTime updatedUtc) =>
        File.WriteAllText(Path.Combine(_updateDir, "status.json"), $$"""
            { "phase": "{{phase}}", "message": "", "startedUtc": "{{updatedUtc:O}}", "updatedUtc": "{{updatedUtc:O}}" }
            """);

    private static string ReleaseJson(string tag, string? htmlUrl = null)
    {
        var version = tag.TrimStart('v');
        return JsonSerializer.Serialize(new
        {
            tag_name = tag,
            draft = false,
            prerelease = false,
            body = "### Neu\n- Etwas",
            html_url = htmlUrl,
            assets = new[]
            {
                new { name = $"HorizonNET-{version}-win-x64.zip", size = 100, state = "uploaded" },
                new { name = $"HorizonNET-{version}-win-x64.zip.sha256", size = 1, state = "uploaded" },
            },
        });
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private HttpStatusCode _status = HttpStatusCode.NotFound;
        private string _body = "{}";
        private bool _brokenBody;

        public Exception? Throw { get; set; }

        public int Calls { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }

        public void Respond(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public void RespondWithBrokenBody()
        {
            _status = HttpStatusCode.OK;
            _brokenBody = true;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            LastRequest = request;
            if (Throw is not null) throw Throw;
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = _brokenBody
                    ? new StreamContent(new BrokenStream())
                    : new StringContent(_body, Encoding.UTF8, "application/json"),
            });
        }
    }

    // Wie eine Verbindung, die nach den Headern abreißt.
    private sealed class BrokenStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) =>
            throw new HttpIOException(HttpRequestError.ResponseEnded, "Verbindung abgerissen");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new HttpIOException(HttpRequestError.ResponseEnded, "Verbindung abgerissen");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FakeStarter : IUpdateTaskStarter
    {
        public bool Fail { get; set; }
        public string? Started { get; private set; }

        public Task<(bool Success, string Message)> StartAsync(string taskName, CancellationToken ct)
        {
            if (Fail) return Task.FromResult((false, "Zugriff verweigert"));
            Started = taskName;
            return Task.FromResult((true, ""));
        }
    }
}
