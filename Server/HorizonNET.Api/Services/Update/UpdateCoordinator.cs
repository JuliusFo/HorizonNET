using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using HorizonNET.Shared.Transfer.Update;
using Microsoft.Extensions.Options;

namespace HorizonNET.Api.Services.Update;

// Stand für die Einstellungen und das Auslösen der Installation. Installiert wird nicht hier:
// Die API schreibt update\request\request.json und startet die geplante Aufgabe; den Rest macht
// der Updater als SYSTEM (Tools/HorizonNET.Updater). Der Dienst selbst hat keine Admin-Rechte.
public sealed class UpdateCoordinator(
    UpdateCheckService checker,
    IUpdateTaskStarter taskStarter,
    IOptions<UpdateOptions> options,
    AppVersionInfo appVersion,
    TimeProvider time,
    ILogger<UpdateCoordinator> logger)
{
    // Ein Updater-Lauf, der so lange nichts mehr in status.json schreibt, hängt vermutlich.
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(30);
    private const int MaxStatusBytes = 64 * 1024;

    private static readonly JsonSerializerOptions StatusJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly SemaphoreSlim _installGate = new(1, 1);

    public UpdateInfoDto GetInfo()
    {
        var mode = options.Value.Mode;
        var check = checker.Last;
        var lastRun = ReadStatus();
        var stale = lastRun is not null && UpdatePhases.IsRunning(lastRun.Phase)
                    && time.GetUtcNow().UtcDateTime - lastRun.UpdatedUtc > StaleAfter;
        var installing = lastRun is not null && UpdatePhases.IsRunning(lastRun.Phase) && !stale;

        return new UpdateInfoDto(
            mode,
            appVersion.Informational,
            check.CheckedUtc,
            check.Error,
            check.Available,
            lastRun,
            installing,
            CanInstall: mode == UpdateMode.Install && check.Available is not null && !installing
                        && options.Value.Directory is not null);
    }

    public async Task<InstallRequestResult> RequestInstallAsync(CancellationToken ct)
    {
        var settings = options.Value;
        if (settings.Mode != UpdateMode.Install || settings.Directory is null)
            return InstallRequestResult.Rejected("Installieren ist auf diesem Server nicht eingerichtet (Update:Mode).");

        await _installGate.WaitAsync(ct);
        try
        {
            var info = GetInfo();
            if (info.IsInstalling) return InstallRequestResult.Rejected("Ein Update läuft bereits.");
            if (info.Available is null) return InstallRequestResult.Rejected("Kein Update verfügbar.");

            // Nur der Wunsch, keine Version: Der Updater holt selbst das neueste Release.
            var requestDir = Path.Combine(settings.Directory, "request");
            var requestFile = Path.Combine(requestDir, "request.json");
            var tmp = requestFile + ".tmp";
            await File.WriteAllBytesAsync(tmp, JsonSerializer.SerializeToUtf8Bytes(
                new UpdateRequest(UpdateRequest.Install, time.GetUtcNow().UtcDateTime), StatusJson), ct);
            File.Move(tmp, requestFile, overwrite: true);

            var started = await taskStarter.StartAsync(settings.TaskName, ct);
            if (!started.Success)
            {
                TryDelete(requestFile);
                logger.LogWarning("Update-Aufgabe ließ sich nicht starten: {Message}", started.Message);
                return InstallRequestResult.Failed($"Die Update-Aufgabe ließ sich nicht starten: {started.Message}");
            }

            logger.LogInformation("Update auf {Version} angefordert.", info.Available.Version);
            return InstallRequestResult.Ok;
        }
        finally
        {
            _installGate.Release();
        }
    }

    private UpdateRunStatus? ReadStatus()
    {
        var directory = options.Value.Directory;
        if (directory is null) return null;
        var path = Path.Combine(directory, "status.json");
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length > MaxStatusBytes) return null;
            return JsonSerializer.Deserialize<UpdateRunStatus>(File.ReadAllBytes(path), StatusJson);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning("status.json nicht lesbar: {Message}", ex.Message);
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { /* Der Updater verwirft sie beim nächsten Lauf ohnehin. */ }
    }
}

public sealed record InstallRequestResult(bool Success, bool IsServerError, string? Message)
{
    public static InstallRequestResult Ok { get; } = new(true, false, null);
    public static InstallRequestResult Rejected(string message) => new(false, false, message);
    public static InstallRequestResult Failed(string message) => new(false, true, message);
}

public interface IUpdateTaskStarter
{
    Task<(bool Success, string Message)> StartAsync(string taskName, CancellationToken ct);
}

// schtasks /run: Das Dienstkonto darf genau diese Aufgabe starten (server-install.ps1 setzt das
// Recht im Security Descriptor der Aufgabe).
public sealed class SchtasksUpdateTaskStarter : IUpdateTaskStarter
{
    public async Task<(bool Success, string Message)> StartAsync(string taskName, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) return (false, "nur unter Windows möglich");
        return await RunAsync(taskName, ct);
    }

    [SupportedOSPlatform("windows")]
    private static async Task<(bool, string)> RunAsync(string taskName, CancellationToken ct)
    {
        var info = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[] { "/run", "/tn", taskName }) info.ArgumentList.Add(arg);

        using var process = Process.Start(info);
        if (process is null) return (false, "schtasks.exe ließ sich nicht starten");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        return process.ExitCode == 0 ? (true, "") : (false, (await stderr).Trim());
    }
}
