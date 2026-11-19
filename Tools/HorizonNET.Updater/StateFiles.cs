using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using HorizonNET.Shared.Transfer.Update;

namespace HorizonNET.Updater;

// status.json, journal.json und request.json. Geschrieben wird über eine Temp-Datei und Move, damit
// die API (status.json) und ein späterer Lauf (journal.json) nie eine halbe Datei lesen.
internal sealed class StateFiles(UpdaterLayout layout)
{
    // request.json schreibt das Dienstkonto; mehr als ein paar Byte braucht sie nie.
    private const int MaxRequestBytes = 4 * 1024;

    public UpdateRunStatus? ReadStatus() => Read(layout.StatusFile, UpdaterJson.Default.UpdateRunStatus);

    public void WriteStatus(UpdateRunStatus status) => Write(layout.StatusFile, status, UpdaterJson.Default.UpdateRunStatus);

    public UpdateJournal? ReadJournal() => Read(layout.JournalFile, UpdaterJson.Default.UpdateJournal);

    public void WriteJournal(UpdateJournal journal) => Write(layout.JournalFile, journal, UpdaterJson.Default.UpdateJournal);

    public void DeleteJournal()
    {
        if (File.Exists(layout.JournalFile)) File.Delete(layout.JournalFile);
    }

    // Liest und löscht die Anfrage der API. Die Datei kommt vom Dienstkonto, also von außen:
    // Nur „mode“ zählt, alles andere wird ignoriert, ihr Inhalt landet nie im Log.
    public UpdateRequest? TakeRequest(UpdateLog log)
    {
        var path = layout.RequestFile;
        if (!File.Exists(path)) return null;

        UpdateRequest? request = null;
        try
        {
            var info = new FileInfo(path);
            if (info.Length > MaxRequestBytes)
                log.Warn("Anfrage ist zu groß und wird verworfen.");
            else
                request = JsonSerializer.Deserialize(File.ReadAllBytes(path), UpdaterJson.Default.UpdateRequest);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            log.Warn($"Anfrage nicht lesbar ({ex.GetType().Name}) und verworfen.");
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warn($"Anfrage ließ sich nicht löschen: {ex.Message}");
        }

        return request;
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
