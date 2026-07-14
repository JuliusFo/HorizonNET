namespace HorizonNET.App.Components;

// Farb-Hilfen für Flächen, die mit einer frei wählbaren Arbeitsbereichs-/Projektfarbe
// hinterlegt werden (Kalender-Seitenleiste, Gruppen der Projektübersicht).
public static class ColorDisplay
{
    // Lesbare Schriftfarbe auf der Hintergrundfarbe: helle Flächen brauchen
    // dunkle Schrift (wahrgenommene Helligkeit nach ITU-R BT.601).
    public static string TextColorFor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex) || hex.Length < 7 || hex[0] != '#') return "#fff";

        try
        {
            var r = Convert.ToInt32(hex.Substring(1, 2), 16);
            var g = Convert.ToInt32(hex.Substring(3, 2), 16);
            var b = Convert.ToInt32(hex.Substring(5, 2), 16);
            var luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;
            return luminance > 0.6 ? "#212529" : "#fff";
        }
        catch
        {
            return "#fff";
        }
    }
}
