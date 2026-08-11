using System.Net;
using System.Text.RegularExpressions;

namespace HorizonNET.Shared.Transfer;

// Erzeugt aus HTML eine kurze Klartext-Vorschau für die Notizliste. Bewusst in Shared,
// damit Server (Listen-Endpunkt) und Client (In-Place-Update nach dem Speichern) exakt
// dieselbe Logik nutzen.
public static partial class NoteSnippet
{
    public static string From(string? html, int maxLength = 160)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;

        // Block-Elemente trennen Wörter, Auszeichnungen im Fließtext nicht. Deshalb zwei
        // Durchgänge statt einem: "<p>a</p><p>b</p>" muss zu "a b" werden, "<b>Ur</b>laub"
        // dagegen zu "Urlaub". Ein pauschales Ersetzen durch Leerzeichen zerschnitt Wörter
        // (die Suche fand "Urlaub" dann nicht mehr), ein pauschales Löschen klebte umgekehrt
        // Absätze zusammen ("mitAnna").
        var text = BlockTagRegex().Replace(html, " ");
        text = TagRegex().Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);
        text = WhitespaceRegex().Replace(text, " ").Trim();
        return text.Length > maxLength ? text[..maxLength] + "…" : text;
    }

    // Alles, was im Browser eine neue Zeile beginnt. Der Rest (b, i, u, span, strong, a …)
    // steht im Fließtext und verschwindet ersatzlos.
    [GeneratedRegex(
        @"</?(p|div|br|hr|li|ul|ol|dl|dt|dd|tr|td|th|table|thead|tbody|h[1-6]|blockquote|pre|section|article|header|footer)\b[^>]*>",
        RegexOptions.IgnoreCase)]
    private static partial Regex BlockTagRegex();

    [GeneratedRegex("<.*?>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
