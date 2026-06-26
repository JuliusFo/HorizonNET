using HorizonNET.Shared.Transfer.Enums;

namespace HorizonNET.App.Components;

// Zentrale Symbol-/Farbzuordnung für die Priorität, damit Indikator und
// Dropdowns dieselbe Darstellung verwenden.
public static class PriorityDisplay
{
    public static string Symbol(Priority p) => p switch
    {
        Priority.High   => "↑↑",
        Priority.Medium => "↑",
        Priority.Low    => "↓",
        _               => ""
    };

    public static string CssClass(Priority p) => $"priority-{p.ToString().ToLowerInvariant()}";
}
