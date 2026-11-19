using HorizonNET.Shared.Transfer.Update;

namespace HorizonNET.Updater;

// Alles, was das System berührt, steckt hinter diesen Schnittstellen. Die Abläufe (Preparer,
// Installer) sind so ohne Dienst, Netz und Admin-Rechte testbar, auch unter Linux in der CI.

internal interface IServiceHost
{
    void Stop(TimeSpan timeout);
    void Start(TimeSpan timeout);

    ServiceStartType GetStartType();
    void SetStartType(ServiceStartType startType);

    // Basisadresse des Dienstes auf diesem Rechner, aus ASPNETCORE_URLS der Dienst-Umgebung.
    Uri GetLocalBaseUri();
}

internal interface IHealthProbe
{
    // InformationalVersion aus GET /api/version, null wenn (noch) keine Antwort kommt.
    Task<string?> GetVersionAsync(Uri baseUri, CancellationToken ct);
}

internal interface IReleaseSource
{
    Task<ReleaseCandidate?> GetLatestAsync(CancellationToken ct);
    Task DownloadAsync(ReleaseCandidate release, string zipPath, string checksumPath, CancellationToken ct);
}

internal interface IApplyLauncher
{
    // Startet apply aus dem neuen Paket und wartet nicht darauf.
    void StartApply(string updaterExe, string root, string packageDirectory, int parentProcessId);
}

internal interface IPermissions
{
    // Nach dem Verschieben aus update\staging\ soll ein Ordner die Rechte seines neuen
    // Elternordners tragen, nicht die von staging.
    void ResetToInherited(string directory);

    // Gibt target dieselben Rechte wie source (explizite Einträge und Vererbungsschutz) und
    // vererbt sie an den Inhalt. Für die Rücksicherung von data\ und keys\.
    void CopyAccess(string source, string target);
}

internal interface IInstalledVersion
{
    Version? Read(UpdaterLayout layout);
}

internal enum ServiceStartType
{
    Automatic,
    Manual,
    Disabled,
}

internal sealed record UpdaterTimings(
    TimeSpan ServiceTimeout,
    TimeSpan HealthTimeout,
    TimeSpan PollInterval,
    TimeSpan LockWait,
    TimeSpan IoRetryDelay,
    int IoRetries)
{
    public static UpdaterTimings Default { get; } = new(
        ServiceTimeout: TimeSpan.FromSeconds(60),
        HealthTimeout: TimeSpan.FromSeconds(120),
        PollInterval: TimeSpan.FromSeconds(2),
        LockWait: TimeSpan.FromSeconds(60),
        IoRetryDelay: TimeSpan.FromSeconds(1),
        IoRetries: 15);
}

// Fehler mit einer Meldung, die so in status.json und damit in den Einstellungen stehen darf.
internal sealed class UpdateException(string message) : Exception(message);
