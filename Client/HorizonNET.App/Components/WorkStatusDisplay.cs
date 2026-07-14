using HorizonNET.Shared.Transfer.DTOs;
using HorizonNET.Shared.Transfer.Enums;

namespace HorizonNET.App.Components;

// Anzeige-Reihenfolge der Status – bewusst abweichend von den Enum-Werten, die
// "Geplant" vor "In Arbeit" einsortieren würden. Erledigte Tasks (Fertig/Abgebrochen)
// stehen unabhängig davon immer am Ende, siehe SortedByDefault.
public static class WorkStatusDisplay
{
    public static int SortRank(WorkStatus status) => status switch
    {
        WorkStatus.InProgress      => 0,
        WorkStatus.PlannedToday    => 1,
        WorkStatus.PlannedPriority => 2,
        WorkStatus.Planned         => 3,
        WorkStatus.Paused          => 4,
        WorkStatus.Done            => 5,
        WorkStatus.Abandoned       => 6,
        _                          => 7
    };

    // Standard-Sortierung der Task-Listen: erledigte Tasks nach unten, innerhalb
    // beider Gruppen nach Status, dann Priorität (hoch zuerst), dann Titel A-Z.
    public static IEnumerable<TaskResponseDto> SortedByDefault(this IEnumerable<TaskResponseDto> tasks) =>
        tasks.OrderBy(t => t.IsCompleted)
             .ThenBy(t => SortRank(t.Status))
             .ThenByDescending(t => ParsePriority(t.Priority))
             .ThenBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase);

    // Priority kommt im DTO als String; unbekannte Werte gelten als Medium.
    private static Priority ParsePriority(string value) =>
        Enum.TryParse<Priority>(value, out var priority) ? priority : Priority.Medium;
}
