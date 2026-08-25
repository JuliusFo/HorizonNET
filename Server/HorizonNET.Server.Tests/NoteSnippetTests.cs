using HorizonNET.Shared.Transfer;

namespace HorizonNET.Server.Tests;

// Die HTML→Klartext-Umwandlung aus Shared. Sie hat zwei Nutzer, die sich denselben Text
// teilen sollen: die Vorschau in der Notizliste und die Suche (Notizen wie Journal).
// Deshalb hier direkt geprüft und nicht nur über die Suche mit.
public class NoteSnippetTests
{
    // Der Kern der Regel: Block-Elemente trennen Wörter, Auszeichnungen nicht.
    [Theory]
    [InlineData("<p>Termin mit</p><p>Anna</p>", "Termin mit Anna")]   // Absätze trennen
    [InlineData("<p><b>Ur</b>laub</p>", "Urlaub")]                     // Auszeichnung trennt nicht
    [InlineData("<p>Zeile eins<br/>Zeile zwei</p>", "Zeile eins Zeile zwei")]
    [InlineData("<ul><li>Milch</li><li>Brot</li></ul>", "Milch Brot")]
    [InlineData("<p><span style=\"color:red\">rot</span> und blau</p>", "rot und blau")]
    public void From_SeparatesBlocksButNotInlineFormatting(string html, string expected) =>
        Assert.Equal(expected, NoteSnippet.From(html));

    // Gängige Notizen sahen vorher schon richtig aus und dürfen sich nicht verändern –
    // die neue Regel ist eine Ergänzung, kein Umbau.
    [Fact]
    public void From_LeavesOrdinaryTextUnchanged()
    {
        Assert.Equal("Termin mit Anna", NoteSnippet.From("<p>Termin mit <b>Anna</b></p>"));
        Assert.Equal("Nur Text", NoteSnippet.From("Nur Text"));
    }

    [Fact]
    public void From_DecodesEntitiesAndCollapsesWhitespace() =>
        Assert.Equal("Kosten & Nutzen", NoteSnippet.From("<p>Kosten   &amp;\n Nutzen</p>"));

    // Journal.IsBlank und die Notizliste verlassen sich darauf: Ein "leerer" Editor liefert
    // HTML, das keinen sichtbaren Text enthält.
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("<p></p>")]
    [InlineData("<p>&nbsp;</p>")]
    [InlineData("<p><br></p>")]
    public void From_TreatsMarkupWithoutVisibleTextAsEmpty(string? html) =>
        Assert.Equal(string.Empty, NoteSnippet.From(html));

    [Fact]
    public void From_TruncatesWithEllipsis()
    {
        var text = NoteSnippet.From("<p>" + new string('a', 200) + "</p>", maxLength: 10);

        Assert.Equal(new string('a', 10) + "…", text);
    }
}
