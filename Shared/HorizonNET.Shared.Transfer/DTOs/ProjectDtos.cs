using HorizonNET.Shared.Transfer.Enums;

namespace HorizonNET.Shared.Transfer.DTOs;

public record ProjectCreateDto(
    string Name,
    string? Description,
    ProjectStatus Status,
    Priority Priority,
    string? Color = null,
    int? WorkspaceId = null
);

// Vollersatz, deshalb ohne Standardwerte (siehe NoteUpdateDto).
public record ProjectUpdateDto(
    string Name,
    string? Description,
    ProjectStatus Status,
    Priority Priority,
    string? Color,
    int? WorkspaceId
);

public record ProjectResponseDto(
    int Id,
    string Name,
    string? Description,
    string Status,
    string Priority,
    DateTime CreatedAt,
    // Zähler der Projektkarte ("X% erledigt · Y offen"): NUR Haupt-Tasks, keine
    // Sub-Tasks – dieselbe Einheit, die die Task-Liste des Projekts anzeigt. Wer die
    // Formel ändert, muss ProjectsController.ToDto UND SyncProjectCounts
    // (ProjectDetail) gleichziehen.
    int TaskCount,
    int DoneTaskCount,
    string? Color = null,
    int? WorkspaceId = null
);
