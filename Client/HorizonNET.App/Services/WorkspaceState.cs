using HorizonNET.Shared.Transfer.DTOs;

namespace HorizonNET.App.Services;

// Gemeinsame State-Quelle für Arbeitsbereiche, analog zu ProjectState.
// Komponenten abonnieren OnChange und rendern bei Benachrichtigung neu.
public class WorkspaceState(ApiService api)
{
    public List<WorkspaceResponseDto>? Workspaces { get; private set; }

    public event Action? OnChange;

    public async Task EnsureLoadedAsync()
    {
        if (Workspaces is null)
            await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        Workspaces = await api.GetWorkspacesAsync();
        NotifyChanged();
    }

    public void Set(List<WorkspaceResponseDto>? workspaces)
    {
        Workspaces = workspaces;
        NotifyChanged();
    }

    public void NotifyChanged() => OnChange?.Invoke();
}
