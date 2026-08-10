using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HorizonNET.Data;

// Verschlüsselt einzelne Spalten transparent beim Schreiben und entschlüsselt sie beim
// Lesen (ASP.NET DataProtection). Deklarativ im Modell statt in einzelnen Repository-
// Methoden: So kann kein später hinzukommender Zugriffsweg die Verschlüsselung
// versehentlich umgehen.
//
// Das Chiffrat ist base64url-Text – die Spalte bleibt TEXT, es ist keine Schema-Änderung
// nötig. Gleicher Klartext ergibt jedes Mal ein anderes Chiffrat; ein SQL-LIKE darüber
// kann deshalb prinzipiell nicht funktionieren (siehe „Suche trotz Verschlüsselung“ im
// Journal-Konzept).
//
// ⚠ Der DataProtection-Schlüsselring ist der einzige Weg zurück zum Klartext. Geht er
// verloren, sind alle so geschützten Werte unwiederbringlich weg.
public static class EncryptedConverter
{
    // Streng: Was sich nicht entschlüsseln lässt, wirft. Für Inhalte, deren Verlust
    // auffallen MUSS statt still zu verschwinden – ab 12c die Journal-Texte.
    public static ValueConverter<string, string> Strict(IDataProtector protector) =>
        new(plain => protector.Protect(plain),
            stored => protector.Unprotect(stored));

    // Wie Strict, aber für Spalten, die null sein dürfen (Titel, Stimmungsnotiz).
    // Eigene Überladung, weil ein ValueConverter<string, string> an einer string?-Property
    // CS8620 auslöst. null bleibt null – verschlüsselt wird nur echter Inhalt.
    public static ValueConverter<string?, string?> StrictNullable(IDataProtector protector) =>
        new(plain => plain == null ? null : protector.Protect(plain),
            stored => stored == null ? null : protector.Unprotect(stored));

    // Nachsichtig: Nicht entschlüsselbare Werte werden zu string.Empty statt zur Exception.
    // Ausschließlich für Werte, die sich jederzeit neu beschaffen lassen – konkret der
    // Google-Refresh-Token. Dessen Alt-Bestand liegt noch im Klartext in der DB und würde
    // sonst jeden Aufruf, der die Verbindung liest, mit einem 500 quittieren. Leerer Token
    // wird wie „nicht verbunden“ behandelt; einmal neu verbinden schreibt ihn verschlüsselt.
    public static ValueConverter<string, string> Lenient(IDataProtector protector) =>
        new(plain => protector.Protect(plain),
            stored => TryUnprotect(protector, stored));

    private static string TryUnprotect(IDataProtector protector, string stored)
    {
        try
        {
            return protector.Unprotect(stored);
        }
        catch (CryptographicException)
        {
            // Klartext aus der Zeit vor 12a – oder ein anderer Schlüsselring.
            return string.Empty;
        }
    }
}
