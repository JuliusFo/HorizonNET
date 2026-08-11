namespace HorizonNET.Shared.Transfer;

/// <summary>
/// Auslegung von <c>TaskItem.ReminderMinutes</c> bzw. des gleichnamigen DTO-Felds.
/// </summary>
/// <remarks>
/// Bewusst EINE nullable Spalte statt zusätzlich eines "UseDefault"-Schalters: Zwei Felder
/// können sich widersprechen (Schalter an, Minuten gesetzt), eines nicht.
/// <code>
///   null   → erbt die Standard-Erinnerung aus den Einstellungen
///   None   → an diesem Task ausdrücklich KEINE Erinnerung
///   &gt;= 0  → Minuten Vorlauf (0 = zum Termin)
/// </code>
/// Der Sonderwert ist nötig, weil 0 schon "zum Termin" bedeutet und damit nicht zugleich
/// "gar nicht" heißen kann.
/// </remarks>
public static class TaskReminder
{
    public const int None = -1;

    // Kleinster erlaubter Wert (der Sonderwert selbst); nach oben lässt Google
    // vier Wochen zu. Beides prüft der Controller, bevor gespeichert wird.
    public const int MinValue = None;
    public const int MaxValue = 40320;

    public static bool IsValid(int? minutes) =>
        minutes is null || (minutes >= MinValue && minutes <= MaxValue);

    /// <summary>
    /// Tatsächliche Vorlaufzeit aus Task-Wert und Standard; null = keine Erinnerung.
    /// </summary>
    public static int? Effective(int? taskMinutes, int? defaultMinutes) => taskMinutes switch
    {
        null => defaultMinutes,   // nichts am Task gesetzt → Standard
        None => null,             // am Task ausdrücklich abgeschaltet
        _    => taskMinutes
    };
}
