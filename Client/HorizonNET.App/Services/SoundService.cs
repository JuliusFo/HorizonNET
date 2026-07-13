using Microsoft.JSInterop;

namespace HorizonNET.App.Services;

// Spielt kurze UI-Sounds über das JS-Modul wwwroot/js/sounds.js.
// Respektiert die Einstellung SettingsState.SoundsEnabled; Fehler sind unkritisch.
public class SoundService(IJSRuntime js, SettingsState settings) : IAsyncDisposable
{
    private IJSObjectReference? module;

    private async Task<IJSObjectReference> ModuleAsync() =>
        module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/sounds.js");

    private async Task PlayAsync(string name)
    {
        await settings.EnsureLoadedAsync(); // Stumm-Einstellung sicher geladen
        if (!settings.SoundsEnabled) return;

        try
        {
            var m = await ModuleAsync();
            await m.InvokeVoidAsync("playSound", name);
        }
        catch
        {
            // Sound ist optionales Feedback – Interop-/Audio-Fehler ignorieren.
        }
    }

    public Task SuccessAsync() => PlayAsync("success");     // Task erledigt
    public Task DailyAsync() => PlayAsync("daily");         // Daily abgehakt
    public Task CelebrateAsync() => PlayAsync("celebrate"); // alle Dailies geschafft
    public Task ErrorAsync() => PlayAsync("error");         // Fehler/Warnung

    public async ValueTask DisposeAsync()
    {
        if (module is not null)
        {
            try { await module.DisposeAsync(); } catch { /* Teardown */ }
        }
    }
}
