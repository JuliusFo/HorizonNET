using HorizonNET.Shared.Transfer.Enums;

namespace HorizonNET.Shared.Transfer.DTOs;

public record TaskCreateDto(
    string Title,
    string? Description,
    DateTime? DueDate,
    DateTime? StartTime,
    DateTime? EndTime,
    Priority Priority,
    int? ProjectId,
    int? ParentTaskId = null,
    WorkStatus Status = WorkStatus.Planned
);

// Vollersatz aller Felder – nur für die echten Editoren (Detailseite, Bearbeiten-Dialog),
// die auch wirklich alles anzeigen. Wer nur ein Anliegen hat, nimmt eines der Teil-Updates
// darunter: sonst schickt er einen kompletten, womöglich veralteten Stand zurück und rollt
// damit fremde Änderungen zurück.
//
// Link hat bewusst KEINEN Default: mit einem würden Aufrufer klaglos kompilieren und den
// Link stillschweigend löschen. Ohne Default zwingt der Compiler zur Entscheidung.
public record TaskUpdateDto(
    string Title,
    string? Description,
    DateTime? DueDate,
    DateTime? StartTime,
    DateTime? EndTime,
    WorkStatus Status,
    Priority Priority,
    int? ProjectId,
    string? Link
);

// ── Teil-Updates ────────────────────────────────────────────────────────────────
// Jedes ändert genau ein Anliegen und lässt alle übrigen Felder unberührt. Neue Felder
// am Task betreffen diese DTOs deshalb nie – anders als TaskUpdateDto.

// Abhaken, Statuswechsel im Dropdown. Der Server zieht Timer und Fälligkeit nach.
public record TaskStatusDto(WorkStatus Status);

// Termin: Kalender-Drag, "auf heute schieben". Ohne DueDate verwirft der Server die Uhrzeiten.
public record TaskScheduleDto(DateTime? DueDate, DateTime? StartTime, DateTime? EndTime);

// Task einem anderen Projekt zuordnen (bzw. mit null in die Inbox).
public record TaskProjectDto(int? ProjectId);

public record TaskResponseDto(
    int Id,
    string Title,
    string? Description,
    DateTime? DueDate,
    DateTime? StartTime,
    DateTime? EndTime,
    WorkStatus Status,
    string Priority,
    int? ProjectId,
    string? ProjectName,
    // Optionaler externer Link; null = nicht gesetzt.
    string? Link,
    int SortOrder = 0,
    // Position in der Projektliste (nur Haupt-Tasks); getrennt von der
    // Kanban-Position SortOrder, siehe TaskItem.
    int ListSortOrder = 0,
    int? ParentTaskId = null,
    List<TaskResponseDto>? SubTasks = null,
    DateTime CreatedAt = default,
    DateTime UpdatedAt = default,
    // Ist der Task aktuell in den Google-Kalender gespiegelt? (Server leitet es aus
    // dem Vorhandensein einer GoogleEventId ab; nur Lese-Richtung.)
    bool IsSyncedToGoogle = false,
    // Zeiterfassung: Summe der abgeschlossenen Intervalle in Sekunden.
    int TrackedSeconds = 0,
    // Startzeitpunkt des laufenden Intervalls; null = Timer läuft nicht.
    DateTime? RunningSince = null
)
{
    public bool IsCompleted => Status == WorkStatus.Done || Status == WorkStatus.Abandoned;

    public bool IsTimerRunning => RunningSince is not null;

    // Gesamtzeit inkl. des noch laufenden Intervalls (für die tickende Anzeige).
    public TimeSpan TrackedTotal(DateTime now) => TimeSpan.FromSeconds(TrackedSeconds)
        + (RunningSince is DateTime since ? now - since : TimeSpan.Zero);
}

// Neue Reihenfolge einer Kanban-Spalte: die Task-Ids in gewünschter
// Reihenfolge; der Server setzt SortOrder = Index und Status = Status.
public record TaskReorderDto(
    WorkStatus Status,
    List<int> OrderedTaskIds
);
