namespace HorizonNET.App.Components;

// Die fünf Stufen der Stimmungsskala an genau einer Stelle: Emoji, Farbe, Bezeichnung.
//
// Bewusst zentral, weil dieselbe Skala an mehreren Orten auftaucht – Erfassungszeile und
// Tages-Zeitstrahl (14f), später Mehrtages-Band (14g), Jahres-Heatmap (14j) und
// Wochenrückblick (14n). Eine 4 muss überall gleich aussehen, sonst liest man die
// Farben nicht mehr als Aussage, sondern als Zufall.
//
// Es sind Stufen einer Skala, KEINE benannten Emotionen: Nur so lassen sich Werte
// mitteln und als Kurve zeichnen. Benannte Emotionen sind bewusst zurückgestellt
// (siehe docs/konzept-journal.md, "Bewusst nicht im Scope").
public static class MoodScale
{
    public const byte Min = 1;
    public const byte Max = 5;

    public static readonly byte[] Values = [1, 2, 3, 4, 5];

    public static string Emoji(byte mood) => mood switch
    {
        1 => "😞",
        2 => "😕",
        3 => "😐",
        4 => "🙂",
        _ => "😄"
    };

    public static string Label(byte mood) => mood switch
    {
        1 => "sehr schlecht",
        2 => "schlecht",
        3 => "neutral",
        4 => "gut",
        _ => "sehr gut"
    };

    // Rot → Grau → Grün. Die Mitte bewusst neutral-grau statt gelb: Gelb liest sich
    // als Warnung, "neutral" ist aber keine.
    public static string Color(byte mood) => mood switch
    {
        1 => "#c0392b",
        2 => "#e07b39",
        3 => "#8d949e",
        4 => "#7cb342",
        _ => "#3d8b40"
    };

    // Tagesmittel für Heatmap und Rückblick – rundet auf die nächste Stufe.
    public static string ColorForAverage(double average) =>
        Color((byte)Math.Clamp(Math.Round(average, MidpointRounding.AwayFromZero), Min, Max));
}
