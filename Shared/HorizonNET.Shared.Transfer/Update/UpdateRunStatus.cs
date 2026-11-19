namespace HorizonNET.Shared.Transfer.Update;

// Inhalt von update\status.json: Der Updater schreibt ihn, die API liest ihn für die
// Einstellungen. JSON mit camelCase und der Phase als Text.
public record UpdateRunStatus(
    UpdatePhase Phase,
    string? FromVersion,
    string? TargetVersion,
    string Message,
    DateTime StartedUtc,
    DateTime UpdatedUtc,
    DateTime? FinishedUtc);

public enum UpdatePhase
{
    // Läuft
    Preparing,
    Downloading,
    Installing,

    // Fertig
    Succeeded,
    NoUpdate,
    Failed,          // nichts geändert (z. B. Download oder Prüfsumme)
    RolledBack,      // installiert, gescheitert, alter Stand wiederhergestellt
    RollbackFailed,  // auch das Zurückrollen ist gescheitert – Handarbeit nötig
}

public static class UpdatePhases
{
    public static bool IsRunning(UpdatePhase phase) =>
        phase is UpdatePhase.Preparing or UpdatePhase.Downloading or UpdatePhase.Installing;
}

// Inhalt von update\request\request.json, geschrieben von der API. Mehr als den Wunsch
// „installieren“ nimmt der Updater daraus nicht: keine Version, keine Pfade, keine URLs.
public record UpdateRequest(string Mode, DateTime RequestedUtc)
{
    public const string Install = "install";
}
