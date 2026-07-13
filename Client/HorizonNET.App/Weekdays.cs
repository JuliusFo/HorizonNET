namespace HorizonNET.App;

// Helfer rund um die Wochentags-Bitmaske der Daily Tasks.
// Bit-Index = (int)DayOfWeek (So=0 … Sa=6); Anzeige/Reihenfolge Mo..So (deutsch).
public static class Weekdays
{
    public const byte Daily = 127;      // alle sieben Tage
    public const byte MonToFri = 62;    // Mo|Di|Mi|Do|Fr (Bits 1..5)

    // Reihenfolge für die Anzeige: Montag zuerst.
    public static readonly (DayOfWeek Day, string Short)[] Order =
    [
        (DayOfWeek.Monday, "Mo"),
        (DayOfWeek.Tuesday, "Di"),
        (DayOfWeek.Wednesday, "Mi"),
        (DayOfWeek.Thursday, "Do"),
        (DayOfWeek.Friday, "Fr"),
        (DayOfWeek.Saturday, "Sa"),
        (DayOfWeek.Sunday, "So"),
    ];

    private static int Bit(DayOfWeek d) => 1 << (int)d;

    public static bool IsSet(byte mask, DayOfWeek d) => (mask & Bit(d)) != 0;

    public static byte Toggle(byte mask, DayOfWeek d) => (byte)(mask ^ Bit(d));

    // Kompakte Darstellung der gesetzten Tage, z. B. "Mo Mi Fr".
    public static string Format(byte mask) =>
        string.Join(" ", Order.Where(o => IsSet(mask, o.Day)).Select(o => o.Short));
}
