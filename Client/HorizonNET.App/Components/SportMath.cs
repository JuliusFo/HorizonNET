using HorizonNET.Shared.Transfer.DTOs;
using HorizonNET.Shared.Transfer.Enums;

namespace HorizonNET.App.Components;

// Kennzahlen der Sport-Auswertung. Bewusst eine eigene, reine Klasse: "Fortschritt"
// bedeutet je Übungsart etwas anderes, und diese Regeln sollen an einer Stelle stehen
// statt verstreut in den Charts.
public static class SportMath
{
    // Volumen eines Kraftsatzes = Wiederholungen × Gewicht. Über eine Einheit summiert
    // die klassische Fortschrittszahl.
    public static double Volume(ExerciseSetResponseDto s) =>
        (s.Reps ?? 0) * (s.WeightKg ?? 0);

    // Geschätzte Maximalkraft (1RM) nach Epley: Gewicht × (1 + Wdh / 30).
    // Der Sinn: eine EINZIGE vergleichbare Zahl, auch wenn mal 3×12 mit 10 kg und mal
    // 3×8 mit 15 kg trainiert wird – ohne sie vergleicht man Äpfel mit Birnen.
    // Bei genau einer Wiederholung liefert die Formel exakt das Gewicht.
    public static double? EstimatedOneRepMax(ExerciseSetResponseDto s)
    {
        if (s.Reps is not > 0 || s.WeightKg is not > 0) return null;
        return s.WeightKg.Value * (1 + s.Reps.Value / 30.0);
    }

    // Pace in Sekunden je Kilometer. Die eigentliche Fortschrittskurve beim Laufen –
    // eine längere Strecke ist ja kein schnelleres Laufen.
    public static double? PaceSecondsPerKm(ExerciseSetResponseDto s)
    {
        if (s.DistanceMeters is not > 0 || s.DurationSeconds is not > 0) return null;
        return s.DurationSeconds.Value / (s.DistanceMeters.Value / 1000.0);
    }

    // Der Leitwert einer Einheit (= alle Sätze eines Tages) je Übungsart:
    //   Kraft          → bestes geschätztes 1RM des Tages
    //   Körpergewicht  → Wiederholungen gesamt
    //   Ausdauer       → Pace (Gesamtstrecke / Gesamtdauer, nicht Mittel der Einzel-Paces)
    // null = an dem Tag nicht berechenbar (z. B. Lauf ohne Dauer).
    public static double? DailyMetric(ExerciseKind kind, IReadOnlyCollection<ExerciseSetResponseDto> sets)
    {
        if (sets.Count == 0) return null;

        switch (kind)
        {
            case ExerciseKind.Strength:
                var maxima = sets.Select(EstimatedOneRepMax).Where(v => v is not null).ToList();
                return maxima.Count > 0 ? maxima.Max() : null;

            case ExerciseKind.Bodyweight:
                return sets.Sum(s => s.Reps ?? 0);

            case ExerciseKind.Endurance:
                var meters = sets.Sum(s => s.DistanceMeters ?? 0);
                var seconds = sets.Sum(s => s.DurationSeconds ?? 0);
                if (meters <= 0 || seconds <= 0) return null;
                return seconds / (meters / 1000.0);

            default:
                return null;
        }
    }

    // Ein Punkt je Trainingstag – nicht je Satz, sonst zappelt die Kurve. Geteilt von
    // Übungs-Detail und Auswertung, damit beide dieselbe Kurve zeigen.
    public static List<DayMetric> DailySeries(ExerciseKind kind, IEnumerable<ExerciseSetResponseDto> sets) =>
        sets.GroupBy(s => s.PerformedAt.Date)
            .OrderBy(g => g.Key)
            .Select(g => new { g.Key, Metric = DailyMetric(kind, g.ToList()) })
            .Where(x => x.Metric is not null)
            .Select(x => new DayMetric(x.Key, x.Metric!.Value, ChartValue(kind, x.Metric.Value)))
            .ToList();

    // Achsentauglicher Wert: Die Pace liegt als Sekunden/km vor – für die Achse in
    // Minuten, sonst stünden dort 336 statt 5:36 und die Kurve wäre nicht lesbar.
    public static double ChartValue(ExerciseKind kind, double metric) =>
        kind == ExerciseKind.Endurance ? Math.Round(metric / 60.0, 2) : Math.Round(metric, 1);

    // Bestwert einer Serie – bei der Pace ist das der KLEINSTE Wert.
    public static double? Best(ExerciseKind kind, IReadOnlyCollection<DayMetric> series)
    {
        if (series.Count == 0) return null;
        return LowerIsBetter(kind) ? series.Min(p => p.Value) : series.Max(p => p.Value);
    }

    // Value = Rohwert (Bestwert, Formatierung), Display = Wert für die Chart-Achse.
    public record DayMetric(DateTime Day, double Value, double Display);

    // Beschriftung des Leitwerts – gehört zur Kennzahl und wird von Chart und Liste geteilt.
    public static string MetricLabel(ExerciseKind kind) => kind switch
    {
        ExerciseKind.Strength => "Geschätztes 1RM (kg)",
        ExerciseKind.Bodyweight => "Wiederholungen gesamt",
        ExerciseKind.Endurance => "Pace (min/km)",
        _ => "Wert"
    };

    // Bei der Pace ist WENIGER besser – das dreht die Lesart von "Fortschritt" um und
    // muss deshalb an der Kennzahl hängen, nicht im Chart geraten werden.
    public static bool LowerIsBetter(ExerciseKind kind) => kind == ExerciseKind.Endurance;

    // ── Formatierung ───────────────────────────────────────────────────────────

    // Sekunden → "m:ss" (Pace) bzw. "h:mm:ss" (Dauer ab einer Stunde).
    public static string Duration(int seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes}:{t.Seconds:00}";
    }

    public static string Pace(double secondsPerKm) => $"{Duration((int)Math.Round(secondsPerKm))} min/km";

    public static string Distance(int meters) =>
        meters >= 1000 ? $"{meters / 1000.0:0.##} km" : $"{meters} m";

    // Leitwert für die Anzeige, je Art passend formatiert.
    public static string FormatMetric(ExerciseKind kind, double value) => kind switch
    {
        ExerciseKind.Strength => $"{value:0.#} kg",
        ExerciseKind.Bodyweight => $"{value:0} Wdh",
        ExerciseKind.Endurance => Pace(value),
        _ => value.ToString("0.#")
    };

    // Kurzfassung eines Satzes für Listen ("12 × 12,5 kg", "30 Wdh", "5 km · 28:00").
    public static string Summary(ExerciseSetResponseDto s) => s.Kind switch
    {
        ExerciseKind.Strength => $"{s.Reps} × {s.WeightKg:0.##} kg",
        ExerciseKind.Bodyweight => s.WeightKg is > 0
            ? $"{s.Reps} Wdh (+{s.WeightKg:0.##} kg)"
            : $"{s.Reps} Wdh",
        ExerciseKind.Endurance => string.Join(" · ", new[]
        {
            s.DistanceMeters is int m ? Distance(m) : null,
            s.DurationSeconds is int d ? Duration(d) : null
        }.Where(p => p is not null)),
        _ => string.Empty
    };
}
