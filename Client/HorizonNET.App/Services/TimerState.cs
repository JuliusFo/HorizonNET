using HorizonNET.Shared.Transfer.DTOs;

namespace HorizonNET.App.Services;

// Gemeinsamer State für den (systemweit einzigen) laufenden Timer. Die Navigationsleiste
// zeigt ihn an. Maßgeblich ist der Server: Weil ein Statuswechsel die Zeiterfassung
// startet oder stoppt – auch an einem anderen Task, der dabei verdrängt wird – hört
// dieser State auf ApiService.TaskChanged und holt den Stand dann neu.
public class TimerState
{
    private readonly ApiService api;

    public TimerState(ApiService api)
    {
        this.api = api;
        api.TaskChanged += RefreshAsync;
    }

    public RunningTimerDto? Running { get; private set; }

    public bool IsLoaded { get; private set; }

    public event Action? OnChange;

    public bool IsRunningFor(int taskId) => Running?.TaskItemId == taskId;

    public async Task EnsureLoadedAsync()
    {
        if (!IsLoaded) await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        Running = await api.GetRunningTimerAsync();
        IsLoaded = true;
        NotifyChanged();
    }

    public void NotifyChanged() => OnChange?.Invoke();
}
