using HorizonNET.Shared.Transfer.Enums;

namespace HorizonNET.Shared.Transfer.DTOs;

// ── Übungen (Stammdaten) ────────────────────────────────────────────────────────

public record ExerciseResponseDto(
    int Id,
    string Name,
    ExerciseKind Kind,
    string? Notes,
    bool IsActive,
    int SortOrder,
    DateTime CreatedAt
);

public record ExerciseCreateDto(
    string Name,
    ExerciseKind Kind,
    string? Notes
);

// Kind fehlt bewusst: Die Art bestimmt, welche Felder der bereits erfassten Sätze
// belegt sind – ein nachträglicher Wechsel würde die Historie unlesbar machen.
public record ExerciseUpdateDto(
    string Name,
    string? Notes,
    bool IsActive
);

// ── Sätze ───────────────────────────────────────────────────────────────────────

public record ExerciseSetResponseDto(
    int Id,
    int ExerciseId,
    string ExerciseName,
    ExerciseKind Kind,
    DateTime PerformedAt,
    int SetNumber,
    int? Reps,
    double? WeightKg,
    int? DistanceMeters,
    int? DurationSeconds,
    int? Rpe,
    string? Notes
);

// SetNumber fehlt: die vergibt der Server fortlaufend je Übung und Tag.
public record ExerciseSetCreateDto(
    int ExerciseId,
    DateTime PerformedAt,
    int? Reps,
    double? WeightKg,
    int? DistanceMeters,
    int? DurationSeconds,
    int? Rpe,
    string? Notes
);

public record ExerciseSetUpdateDto(
    DateTime PerformedAt,
    int? Reps,
    double? WeightKg,
    int? DistanceMeters,
    int? DurationSeconds,
    int? Rpe,
    string? Notes
);

// ── Körpergewicht ───────────────────────────────────────────────────────────────

public record BodyWeightResponseDto(
    int Id,
    DateOnly MeasuredOn,
    double WeightKg
);

// Kein Update-DTO: Ein zweiter Wert für denselben Tag ist eine Korrektur und
// überschreibt (siehe BodyWeightRepository.SetAsync).
public record BodyWeightSetDto(
    DateOnly MeasuredOn,
    double WeightKg
);
