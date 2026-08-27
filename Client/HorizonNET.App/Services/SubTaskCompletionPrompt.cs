using HorizonNET.Shared.Transfer.DTOs;
using HorizonNET.Shared.Transfer.Enums;

namespace HorizonNET.App.Services;

// Rückfrage beim Abschließen eines Haupt-Tasks mit offenen Sub-Tasks. Hintergrund:
// Die Projektkarte zählt Haupt- UND Sub-Tasks – ein "Fertig" mit zurückbleibenden
// offenen Sub-Tasks ließe die Kachel weiter "offen" zählen, obwohl die Task-Liste
// nichts Offenes mehr zeigt (die Sub-Tasks stecken dann unter der zugeklappten
// Erledigt-Gruppe). Deshalb fragt jeder Abschluss-Weg (Projektliste, Kanban-Board,
// Heute-Seite, Kalender, Bearbeiten-Dialog) über diese eine Stelle nach.
//
// Bewusst KEIN dritter "Abbrechen"-Ausgang: Der Statuswechsel des Haupt-Tasks selbst
// steht nicht zur Debatte (den hat der Nutzer gerade ausgelöst), nur das Mitnehmen
// der Sub-Tasks. Ein Abbruch müsste sonst an jeder Aufrufstelle das jeweilige
// UI-Element (Dropdown, Haken, gezogene Karte) zurücksetzen.
public static class SubTaskCompletionPrompt
{
    // true = offene Sub-Tasks mit abschließen, false = nur den Haupt-Task.
    // Ohne offene Sub-Tasks, für Sub-Tasks selbst, für bereits abgeschlossene Tasks
    // und für nicht abschließende Zielstatus kommt ohne Dialog false zurück – der
    // Aufruf ist damit an jeder Stelle bedingungslos möglich.
    public static async Task<bool> AskAsync(ConfirmService confirm, TaskResponseDto task, WorkStatus target)
    {
        if (target is not (WorkStatus.Done or WorkStatus.Abandoned)) return false;
        if (task.IsCompleted || task.ParentTaskId is not null) return false;

        var open = task.SubTasks?.Count(s => !s.IsCompleted) ?? 0;
        if (open == 0) return false;

        var verb     = target == WorkStatus.Done ? "abschließen" : "verwerfen";
        var partizip = target == WorkStatus.Done ? "abgeschlossen" : "verworfen";
        var message  = open == 1
            ? $"„{task.Title}“ hat noch einen offenen Sub-Task. Soll dieser mit {partizip} werden?"
            : $"„{task.Title}“ hat noch {open} offene Sub-Tasks. Sollen diese mit {partizip} werden?";

        return await confirm.ShowAsync(
            "Offene Sub-Tasks",
            message,
            confirmLabel: $"Mit Sub-Tasks {verb}",
            danger: false,
            cancelLabel: "Nur diesen Task");
    }
}
