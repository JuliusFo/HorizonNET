namespace HorizonNET.Shared.Transfer.Update;

// Update:Mode der API (ADR 0007).
public enum UpdateMode
{
    Off,     // nicht prüfen (Standard, auch in der Entwicklung)
    Notify,  // prüfen und melden (Docker)
    Install, // prüfen, melden und auf Klick installieren (vServer mit Updater)
}

// GET /api/update – alles für die Karte „Über & Updates“.
public record UpdateInfoDto(
    UpdateMode Mode,
    string CurrentVersion,
    DateTime? LastCheckUtc,
    string? CheckError,
    UpdateReleaseDto? Available,
    UpdateRunStatus? LastRun,
    bool IsInstalling,
    bool CanInstall);

// Release-Notes sind der CHANGELOG-Abschnitt als Markdown-Text; der Client zeigt sie als Text,
// nie als HTML. Url zeigt nur auf das eigene Repo.
public record UpdateReleaseDto(string Version, string Notes, DateTimeOffset? PublishedAt, string? Url);
