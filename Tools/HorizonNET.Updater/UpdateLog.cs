using System.Globalization;

namespace HorizonNET.Updater;

// Ein Log je Lauf unter update\logs\, dazu die Konsole. Es bleiben die letzten 30 Logs.
internal sealed class UpdateLog : IDisposable
{
    private const int KeepLogs = 30;
    private readonly StreamWriter _writer;
    private readonly TimeProvider _time;

    public UpdateLog(string directory, string command, TimeProvider time)
    {
        _time = time;
        Directory.CreateDirectory(directory);
        Prune(directory);
        var stamp = time.GetUtcNow().UtcDateTime.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        Path = System.IO.Path.Combine(directory, $"update-{stamp}-{command}.log");
        _writer = new StreamWriter(new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
    }

    public string Path { get; }

    public void Info(string message) => Write("INFO ", message);
    public void Warn(string message) => Write("WARN ", message);
    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        var line = $"{_time.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}Z {level} {message}";
        lock (_writer)
        {
            _writer.WriteLine(line);
        }
        Console.WriteLine(line);
    }

    private static void Prune(string directory)
    {
        foreach (var old in Directory.GetFiles(directory, "update-*.log").OrderDescending(StringComparer.Ordinal).Skip(KeepLogs - 1))
        {
            try { File.Delete(old); }
            catch (IOException) { /* Wird beim nächsten Lauf erneut versucht. */ }
        }
    }

    public void Dispose() => _writer.Dispose();
}

// Sperrdatei update\update.lock: Es läuft immer nur ein Updater (run, install oder apply).
internal sealed class UpdateLock : IDisposable
{
    private FileStream? _stream;

    private UpdateLock(FileStream stream) => _stream = stream;

    public static async Task<UpdateLock?> TryAcquireAsync(string path, TimeSpan wait, TimeProvider time)
    {
        var deadline = time.GetUtcNow() + wait;
        while (true)
        {
            try
            {
                return new UpdateLock(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            }
            catch (IOException) when (time.GetUtcNow() < deadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), time);
            }
            catch (IOException)
            {
                return null;
            }
        }
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;
    }
}
