using Microsoft.JSInterop;

namespace HorizonNET.App.Services;

// Bildschirmsperre für das Journal – bewusst NICHT mehr als das (siehe js/journalLock.js).
// Der entsperrte Zustand lebt nur im Speicher: Nach einem Neuladen der Seite ist wieder
// gesperrt. Gespeichert werden im localStorage nur Salz, PIN-Hash und die Zeitspanne.
public class JournalLockState(IJSRuntime js) : IDisposable
{
    private const string HashKey    = "journal.pinHash";
    private const string SaltKey    = "journal.pinSalt";
    private const string TimeoutKey = "journal.lockTimeoutMinutes";

    // Wie oft geprüft wird, ob die Zeitspanne abgelaufen ist. Feiner als nötig wäre
    // Verschwendung – eine halbe Minute Ungenauigkeit fällt bei 5 Minuten nicht auf.
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    private IJSObjectReference? module;
    private System.Timers.Timer? timer;
    private bool loaded;

    private string? pinHash;
    private string? salt;
    private DateTime lastActivity = DateTime.Now;

    // 0 = nie automatisch sperren.
    public int TimeoutMinutes { get; private set; } = 5;

    // Ob überhaupt eine PIN eingerichtet ist. Ohne PIN ist das Journal frei zugänglich.
    public bool IsConfigured => !string.IsNullOrEmpty(pinHash);

    public bool IsUnlocked { get; private set; }

    // Ob der Inhalt gezeigt werden darf.
    public bool IsOpen => !IsConfigured || IsUnlocked;

    public event Action? OnChange;

    private async Task<IJSObjectReference> ModuleAsync() =>
        module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/journalLock.js");

    public async Task EnsureLoadedAsync()
    {
        if (loaded) return;
        loaded = true;

        pinHash = await js.InvokeAsync<string?>("localStorage.getItem", HashKey);
        salt    = await js.InvokeAsync<string?>("localStorage.getItem", SaltKey);

        var stored = await js.InvokeAsync<string?>("localStorage.getItem", TimeoutKey);
        if (int.TryParse(stored, out var minutes) && minutes is >= 0 and <= 240)
            TimeoutMinutes = minutes;

        OnChange?.Invoke();
    }

    // ── Entsperren und Sperren ───────────────────────────────────────────────────

    public async Task<bool> TryUnlockAsync(string pin)
    {
        await EnsureLoadedAsync();
        if (!IsConfigured || salt is null) return true;

        var module = await ModuleAsync();
        var candidate = await module.InvokeAsync<string>("hash", salt, pin);

        if (!string.Equals(candidate, pinHash, StringComparison.Ordinal))
            return false;

        IsUnlocked = true;
        RegisterActivity();
        StartTimer();
        OnChange?.Invoke();
        return true;
    }

    public void Lock()
    {
        if (!IsUnlocked) return;

        IsUnlocked = false;
        StopTimer();
        OnChange?.Invoke();
    }

    public void RegisterActivity() => lastActivity = DateTime.Now;

    // ── PIN verwalten ────────────────────────────────────────────────────────────

    // pin = null oder leer entfernt die Sperre. Ein Salz wird bei jedem Setzen neu
    // erzeugt, damit gleiche PINs auf verschiedenen Geräten nicht gleich aussehen.
    public async Task SetPinAsync(string? pin)
    {
        await EnsureLoadedAsync();

        if (string.IsNullOrWhiteSpace(pin))
        {
            pinHash = null;
            salt = null;
            await js.InvokeVoidAsync("localStorage.removeItem", HashKey);
            await js.InvokeVoidAsync("localStorage.removeItem", SaltKey);
            IsUnlocked = false;
            StopTimer();
            OnChange?.Invoke();
            return;
        }

        var module = await ModuleAsync();
        salt = await module.InvokeAsync<string>("randomSalt");
        pinHash = await module.InvokeAsync<string>("hash", salt, pin);

        await js.InvokeVoidAsync("localStorage.setItem", SaltKey, salt);
        await js.InvokeVoidAsync("localStorage.setItem", HashKey, pinHash);

        // Wer die PIN gerade gesetzt hat, sitzt davor – nicht sofort aussperren.
        IsUnlocked = true;
        RegisterActivity();
        StartTimer();
        OnChange?.Invoke();
    }

    public async Task SetTimeoutAsync(int minutes)
    {
        if (minutes is < 0 or > 240) return;

        TimeoutMinutes = minutes;
        await js.InvokeVoidAsync("localStorage.setItem", TimeoutKey, minutes.ToString());

        StopTimer();
        if (IsUnlocked) StartTimer();

        OnChange?.Invoke();
    }

    // ── Auto-Lock ────────────────────────────────────────────────────────────────

    private void StartTimer()
    {
        if (TimeoutMinutes <= 0) return;

        timer = new System.Timers.Timer(CheckInterval.TotalMilliseconds);
        timer.Elapsed += OnTick;
        timer.AutoReset = true;
        timer.Start();
    }

    private void StopTimer()
    {
        if (timer is null) return;

        timer.Elapsed -= OnTick;
        timer.Stop();
        timer.Dispose();
        timer = null;
    }

    private void OnTick(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (TimeoutMinutes <= 0) return;
        if (DateTime.Now - lastActivity < TimeSpan.FromMinutes(TimeoutMinutes)) return;

        Lock();
    }

    public void Dispose() => StopTimer();
}
