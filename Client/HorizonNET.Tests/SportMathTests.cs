using HorizonNET.App.Components;
using HorizonNET.Shared.Transfer.DTOs;
using HorizonNET.Shared.Transfer.Enums;

namespace HorizonNET.Tests;

// Reine Kennzahlen-Logik der Sport-Auswertung. Kein DOM, kein DI – nur Rechnen, deshalb
// gut als klassischer Unit-Test. Prüft die Regeln, die man der Zahl im Chart nicht ansieht.
public class SportMathTests
{
    // ── Geschätztes 1RM (Epley: Gewicht × (1 + Wdh/30)) ──────────────────────────

    [Fact]
    public void OneRepMax_SingleRep_IsEpleyEstimateNotRawWeight()
    {
        // Epley ist eine Schätzung und NICHT exakt bei einer Wiederholung:
        // 100 × (1 + 1/30) = 103,33. Wdh = 1 wird bewusst nicht gesondert behandelt.
        var set = Set(ExerciseKind.Strength, reps: 1, weight: 100);
        Assert.Equal(103.333, SportMath.EstimatedOneRepMax(set)!.Value, 3);
    }

    [Fact]
    public void OneRepMax_MultipleReps_UsesEpley()
    {
        // 100 × (1 + 10/30) = 133,33…
        var set = Set(ExerciseKind.Strength, reps: 10, weight: 100);
        Assert.Equal(133.333, SportMath.EstimatedOneRepMax(set)!.Value, 3);
    }

    [Fact]
    public void OneRepMax_WithoutWeightOrReps_IsNull()
    {
        Assert.Null(SportMath.EstimatedOneRepMax(Set(ExerciseKind.Strength, reps: 8, weight: null)));
        Assert.Null(SportMath.EstimatedOneRepMax(Set(ExerciseKind.Strength, reps: null, weight: 50)));
    }

    // ── Pace (Sekunden je Kilometer) ─────────────────────────────────────────────

    [Fact]
    public void Pace_IsSecondsPerKilometer()
    {
        // 1000 m in 300 s → 300 s/km.
        Assert.Equal(300.0, SportMath.PaceSecondsPerKm(Set(ExerciseKind.Endurance, meters: 1000, seconds: 300))!.Value, 3);
    }

    [Fact]
    public void Pace_WithoutDuration_IsNull()
    {
        Assert.Null(SportMath.PaceSecondsPerKm(Set(ExerciseKind.Endurance, meters: 1000, seconds: null)));
    }

    // ── Tagesleitwert je Übungsart ───────────────────────────────────────────────

    [Fact]
    public void DailyMetric_Strength_IsBestOneRepMaxOfDay()
    {
        // Zwei Sätze, der Leitwert ist das höchste 1RM – nicht das letzte oder ein Mittel.
        var sets = new[]
        {
            Set(ExerciseKind.Strength, reps: 10, weight: 100), // 1RM 133,33
            Set(ExerciseKind.Strength, reps: 1,  weight: 140)  // 1RM 144,67 ← Bestwert (Epley)
        };
        Assert.Equal(144.667, SportMath.DailyMetric(ExerciseKind.Strength, sets)!.Value, 3);
    }

    [Fact]
    public void DailyMetric_Bodyweight_SumsReps()
    {
        var sets = new[]
        {
            Set(ExerciseKind.Bodyweight, reps: 12),
            Set(ExerciseKind.Bodyweight, reps: 8)
        };
        Assert.Equal(20.0, SportMath.DailyMetric(ExerciseKind.Bodyweight, sets)!.Value, 3);
    }

    [Fact]
    public void DailyMetric_Endurance_UsesTotalsNotAverageOfPaces()
    {
        // Der springende Punkt: Pace = Gesamtstrecke / Gesamtdauer, nicht das Mittel der
        // Einzel-Paces. Sonst zählte ein kurzes schnelles Stück so viel wie ein langes.
        var sets = new[]
        {
            Set(ExerciseKind.Endurance, meters: 1000, seconds: 300), // Einzel-Pace 300 s/km
            Set(ExerciseKind.Endurance, meters: 2000, seconds: 300)  // Einzel-Pace 150 s/km
        };
        // Gesamt: 3000 m in 600 s → 200 s/km. (Mittel der Einzel-Paces wäre 225.)
        Assert.Equal(200.0, SportMath.DailyMetric(ExerciseKind.Endurance, sets)!.Value, 3);
    }

    [Fact]
    public void DailyMetric_Endurance_WithoutDuration_IsNull()
    {
        var sets = new[] { Set(ExerciseKind.Endurance, meters: 1000, seconds: null) };
        Assert.Null(SportMath.DailyMetric(ExerciseKind.Endurance, sets));
    }

    [Fact]
    public void DailyMetric_NoSets_IsNull()
    {
        Assert.Null(SportMath.DailyMetric(ExerciseKind.Strength, Array.Empty<ExerciseSetResponseDto>()));
    }

    // ── Bestwert-Richtung ────────────────────────────────────────────────────────
    // Bei der Pace ist WENIGER besser – das dreht die Lesart von "Fortschritt" um.

    [Fact]
    public void LowerIsBetter_OnlyForEndurance()
    {
        Assert.True(SportMath.LowerIsBetter(ExerciseKind.Endurance));
        Assert.False(SportMath.LowerIsBetter(ExerciseKind.Strength));
        Assert.False(SportMath.LowerIsBetter(ExerciseKind.Bodyweight));
    }

    [Fact]
    public void Best_TakesMinForPace_MaxForStrength()
    {
        var strength = new[]
        {
            new SportMath.DayMetric(Day(1), 120, 120),
            new SportMath.DayMetric(Day(2), 140, 140)
        };
        Assert.Equal(140.0, SportMath.Best(ExerciseKind.Strength, strength)!.Value, 3);

        var pace = new[]
        {
            new SportMath.DayMetric(Day(1), 300, 5.0),
            new SportMath.DayMetric(Day(2), 270, 4.5)  // schneller → besser
        };
        Assert.Equal(270.0, SportMath.Best(ExerciseKind.Endurance, pace)!.Value, 3);
    }

    // ── Helfer ───────────────────────────────────────────────────────────────────

    private static ExerciseSetResponseDto Set(
        ExerciseKind kind, int? reps = null, double? weight = null,
        int? meters = null, int? seconds = null) =>
        new(1, 1, "Übung", kind, DateTime.Today, 1, reps, weight, meters, seconds, null, null);

    private static DateTime Day(int n) => new(2026, 1, n);
}
