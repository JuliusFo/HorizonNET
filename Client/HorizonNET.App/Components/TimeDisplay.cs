namespace HorizonNET.App.Components;

// Einheitliche Formatierung erfasster Zeiten.
public static class TimeDisplay
{
    // Kompakt für Listen: "–" (nichts erfasst), "12m", "1h 05m".
    public static string Short(TimeSpan span)
    {
        if (span < TimeSpan.FromMinutes(1)) return span > TimeSpan.Zero ? "<1m" : "–";
        var hours = (int)span.TotalHours;
        return hours > 0 ? $"{hours}h {span.Minutes:00}m" : $"{span.Minutes}m";
    }

    // Laufende Uhr und Intervall-Dauer: "0:05:12" bzw. "1:23:45".
    public static string Clock(TimeSpan span) =>
        $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}";
}
