namespace HorizonNET.Api.Services.Calendar;

// Prozessweiter Zustand des Kalender-Syncs (Singleton): die Sperre, unter der jeder
// Schreibzugriff auf den Kalender und auf TaskItem.CalendarEventId läuft, und die
// Generation des Hintergrund-Abgleichs.
//
// Ohne Sperre sahen ein Task-Update und der parallel laufende Neuabgleich beide
// CalendarEventId == null, legten beide einen Termin an, und eine der IDs ging verloren.
// Ebenso konnte ein Anbieterwechsel mitten im Abgleich eine ID des alten Anbieters
// zurückschreiben. Single-User-App mit einem Prozess: ein SemaphoreSlim genügt.
public sealed class CalendarSyncCoordinator
{
    private int resyncGeneration;

    public CalendarSyncCoordinator() { }

    // DI nimmt diesen Konstruktor: Der Hintergrund-Abgleich endet mit dem Herunterfahren.
    public CalendarSyncCoordinator(IHostApplicationLifetime lifetime) =>
        Stopping = lifetime.ApplicationStopping;

    public SemaphoreSlim Gate { get; } = new(1, 1);

    public CancellationToken Stopping { get; } = CancellationToken.None;

    // Startet eine neue Abgleich-Generation; ein noch laufender älterer Abgleich bricht
    // beim nächsten Task ab, statt mit dem neuen um die Wette zu schreiben.
    public int NextResyncGeneration() => Interlocked.Increment(ref resyncGeneration);

    public bool IsCurrentResync(int generation) => Volatile.Read(ref resyncGeneration) == generation;

    public async Task<IDisposable> LockAsync(CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        return new Releaser(Gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0) gate.Release();
        }
    }
}
