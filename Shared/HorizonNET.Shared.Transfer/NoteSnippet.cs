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
        var text = TagRegex().Replace(html, " ");
        text = WebUtility.HtmlDecode(text);
        text = WhitespaceRegex().Replace(text, " ").Trim();
        return text.Length > maxLength ? text[..maxLength] + "…" : text;
    }

    [GeneratedRegex("<.*?>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
