using HorizonNET.Shared.Transfer.Update;
using Microsoft.JSInterop;

namespace HorizonNET.App.Services;

// Update-Stand für die Karte „Über & Updates“ und den Hinweis-Punkt in der Navigation (ADR 0007).
//
// Nach „Jetzt aktualisieren“ (oder wenn beim Laden schon ein Update läuft) fragt der Client
// leise /api/version und /api/update ab: Der Dienst ist während des Tauschs weg. Meldet die API
// eine neue Version, kommt der „Neu laden“-Toast; endet der Lauf ohne neue Version (zurückgerollt,
// gescheitert), ein Fehler-Toast mit der Meldung des Updaters.
public sealed class UpdateState(ApiService api, VersionState version, ToastService toast, IJSRuntime js) : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan WatchLimit = TimeSpan.FromMinutes(10);

    private bool _loaded;
    private CancellationTokenSource? _watch;

    public UpdateInfoDto? Info { get; private set; }

    public bool Watching => _watch is not null;

    public bool HasUpdate => Info?.Available is not null && Info.Mode != UpdateMode.Off;

    public event Action? OnChange;

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        _loaded = true;
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        Info = await api.GetUpdateInfoAsync();
        OnChange?.Invoke();
        if (Info?.IsInstalling == true) StartWatching();
    }

    public async Task CheckNowAsync()
    {
        var info = await api.CheckForUpdateAsync();
        if (info is not null) Info = info;
        OnChange?.Invoke();
    }

    // null = angefordert, sonst die Meldung des Servers.
    public async Task<string?> InstallAsync()
    {
        var (info, error) = await api.RequestUpdateInstallAsync();
        if (error is not null) return error;
        if (info is not null) Info = info;
        OnChange?.Invoke();
        StartWatching();
        return null;
    }

    private void StartWatching()
    {
        if (_watch is not null) return;
        _watch = new CancellationTokenSource(WatchLimit);
        _ = WatchAsync(_watch.Token);
    }

    private async Task WatchAsync(CancellationToken ct)
    {
        var sawRunning = Info?.IsInstalling == true;
        try
        {
            // Ausgangsversion: die der API, als die Seite geladen wurde.
            await version.EnsureApiLoadedAsync();
            var startVersion = version.ApiVersionShort;

            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(PollInterval, ct);

                var current = await api.GetApiVersionQuietAsync();
                var currentShort = current?.Version.Split('+', 2)[0];
                if (currentShort is not null && startVersion is not null && currentShort != startVersion)
                {
                    toast.ShowUpdate($"HorizonNET {currentShort} ist installiert. Bitte neu laden.",
                        () => js.InvokeVoidAsync("location.reload").AsTask());
                    Info = await api.GetUpdateInfoQuietAsync() ?? Info;
                    return;
                }

                var info = await api.GetUpdateInfoQuietAsync();
                if (info is null) continue; // Dienst gerade weg
                Info = info;
                OnChange?.Invoke();

                if (info.IsInstalling)
                {
                    sawRunning = true;
                    continue;
                }

                // Der Updater ist fertig, die Version hat sich aber nicht geändert.
                if (sawRunning && info.LastRun is { } run && run.Phase != UpdatePhase.Succeeded)
                {
                    toast.ShowError($"Update nicht installiert: {UpdateTexts.Phase(run.Phase)}. {run.Message}");
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Zeitlimit oder Dispose
        }
        finally
        {
            _watch?.Dispose();
            _watch = null;
            OnChange?.Invoke();
        }
    }

    public void Dispose()
    {
        _watch?.Cancel();
    }
}

// Texte rund ums Update, an einer Stelle (und ohne Komponente testbar).
public static class UpdateTexts
{
    public static string Phase(UpdatePhase phase) => phase switch
    {
        UpdatePhase.Preparing => "Vorbereitung",
        UpdatePhase.Downloading => "Download",
        UpdatePhase.Installing => "Installation",
        UpdatePhase.Succeeded => "Erfolgreich",
        UpdatePhase.NoUpdate => "Kein Update nötig",
        UpdatePhase.Failed => "Fehlgeschlagen, nichts geändert",
        UpdatePhase.RolledBack => "Fehlgeschlagen, alter Stand wiederhergestellt",
        UpdatePhase.RollbackFailed => "Fehlgeschlagen, Handarbeit nötig",
        _ => phase.ToString(),
    };

    // Bootstrap-Textfarbe für das Ergebnis des letzten Laufs.
    public static string PhaseCss(UpdatePhase phase) => phase switch
    {
        UpdatePhase.Succeeded or UpdatePhase.NoUpdate => "text-success",
        UpdatePhase.Failed or UpdatePhase.RolledBack => "text-warning-emphasis",
        UpdatePhase.RollbackFailed => "text-danger",
        _ => "text-muted",
    };
}
