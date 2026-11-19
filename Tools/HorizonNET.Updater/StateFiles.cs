using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using HorizonNET.Shared.Transfer.Update;

namespace HorizonNET.Updater;

// status.json und journal.json. Geschrieben wird über eine Temp-Datei und Move, damit die API
// (status.json) und ein späterer Lauf (journal.json) nie eine halbe Datei lesen.
//
// Bewusst keine Datei, die das Dienstkonto schreibt und SYSTEM liest oder löscht: In einem für
// das Dienstkonto beschreibbaren Ordner könnte es Verweise (Junction, Objekt-Manager-Link)
// anlegen und SYSTEM so auf beliebige Dateien lenken (Review 2026-11-19, S1). Den Wunsch
// „installieren“ übermittelt allein der Start der Aufgabe „HorizonNET Update“.
internal sealed class StateFiles(UpdaterLayout layout)
{
    public UpdateRunStatus? ReadStatus() => Read(layout.StatusFile, UpdaterJson.Default.UpdateRunStatus);

    public void WriteStatus(UpdateRunStatus status) => Write(layout.StatusFile, status, UpdaterJson.Default.UpdateRunStatus);

    public UpdateJournal? ReadJournal() => Read(layout.JournalFile, UpdaterJson.Default.UpdateJournal);

    public void WriteJournal(UpdateJournal journal) => Write(layout.JournalFile, journal, UpdaterJson.Default.UpdateJournal);

    public void DeleteJournal()
    {
        if (File.Exists(layout.JournalFile)) File.Delete(layout.JournalFile);
    }

    private static T? Read<T>(string path, JsonTypeInfo<T> type) where T : class
    {
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize(File.ReadAllBytes(path), type);
    }

    private static void Write<T>(string path, T value, JsonTypeInfo<T> type)
    {
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, JsonSerializer.SerializeToUtf8Bytes(value, type));
        File.Move(tmp, path, overwrite: true);
    }
}

// Schreibt status.json und das Log in einem Zug.
internal sealed class StatusReporter(StateFiles state, TimeProvider time, UpdateLog log)
{
    private readonly DateTime _startedUtc = time.GetUtcNow().UtcDateTime;

    public string? FromVersion { get; set; }
    public string? TargetVersion { get; set; }

    public void Report(UpdatePhase phase, string message)
    {
        var now = time.GetUtcNow().UtcDateTime;
        DateTime? finished = UpdatePhases.IsRunning(phase) ? null : now;
        if (phase is UpdatePhase.Failed or UpdatePhase.RollbackFailed or UpdatePhase.RolledBack)
            log.Error($"[{phase}] {message}");
        else
            log.Info($"[{phase}] {message}");
        state.WriteStatus(new UpdateRunStatus(phase, FromVersion, TargetVersion, message, _startedUtc, now, finished));
    }
}
