using Microsoft.JSInterop;

namespace HorizonNET.App.Services;

// Lokale UI-Einstellungen, die im Browser (localStorage) gespeichert werden.
// Reine Client-Einstellungen – nichts davon geht an die API. Komponenten
// abonnieren OnChange und rendern bei Benachrichtigung neu.
public class SettingsState(IJSRuntime js)
{
    private const string CalendarViewKey = "settings.calendarDefaultView";
    private const string SoundsEnabledKey = "settings.soundsEnabled";

    private bool loaded;

    // Standard-Ansicht des Kalenders als Index passend zu den Radzen-Views:
    // 0 = Tag, 1 = Woche, 2 = Monat.
    public int CalendarDefaultView { get; private set; }

    // Ob UI-Sounds abgespielt werden (Default: an).
    public bool SoundsEnabled { get; private set; } = true;

    public event Action? OnChange;

    // Liest die Einstellungen einmalig aus dem localStorage.
    public async Task EnsureLoadedAsync()
    {
        if (loaded) return;
        loaded = true;

        var stored = await js.InvokeAsync<string?>("localStorage.getItem", CalendarViewKey);
        if (int.TryParse(stored, out var index) && index is >= 0 and <= 2)
            CalendarDefaultView = index;

        var sounds = await js.InvokeAsync<string?>("localStorage.getItem", SoundsEnabledKey);
        if (sounds is not null)
            SoundsEnabled = sounds != "false"; // alles außer "false" = an

        OnChange?.Invoke();
    }

    public async Task SetSoundsEnabledAsync(bool enabled)
    {
        SoundsEnabled = enabled;
        await js.InvokeVoidAsync("localStorage.setItem", SoundsEnabledKey, enabled ? "true" : "false");
        OnChange?.Invoke();
    }

    // Speichert die Standard-Kalenderansicht und benachrichtigt Abonnenten.
    public async Task SetCalendarDefaultViewAsync(int index)
    {
        if (index is < 0 or > 2) return;

        CalendarDefaultView = index;
        await js.InvokeVoidAsync("localStorage.setItem", CalendarViewKey, index.ToString());
        OnChange?.Invoke();
    }
}
