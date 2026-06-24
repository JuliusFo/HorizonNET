using HorizonNET.Shared.Transfer.DTOs;

namespace HorizonNET.App.Services;

// Gemeinsame State-Quelle für Projekte, damit z. B. Navigationsleiste und
// Startseite konsistent bleiben. Komponenten abonnieren OnChange und rendern
// bei Benachrichtigung neu.
public class ProjectState(ApiService api)
{
    public List<ProjectResponseDto>? Projects { get; private set; }

    public event Action? OnChange;

    // Lädt die Projekte einmalig (z. B. beim ersten Seitenaufruf).
    public async Task EnsureLoadedAsync()
    {
        if (Projects is null)
            await RefreshAsync();
    }

    // Lädt die Projekte neu von der API und benachrichtigt alle Abonnenten.
    public async Task RefreshAsync()
    {
        Projects = await api.GetProjectsAsync();
        NotifyChanged();
    }

    // Ersetzt die gesamte Liste und benachrichtigt alle Abonnenten.
    public void Set(List<ProjectResponseDto>? projects)
    {
        Projects = projects;
        NotifyChanged();
    }

    // Nach In-Place-Änderungen an der Liste (Add/Remove/Index) aufrufen,
    // damit Abonnenten neu rendern.
    public void NotifyChanged() => OnChange?.Invoke();
}
