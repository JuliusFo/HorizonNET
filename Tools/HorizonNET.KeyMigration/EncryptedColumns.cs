namespace HorizonNET.KeyMigration;

// Die verschlüsselten Spalten, die verify und reprotect bearbeiten. Tabelle, Spalte und
// Purpose-String MÜSSEN mit dem AppDbContext übereinstimmen – ein neuer verschlüsselter
// Wert im Modell gehört auch hier eingetragen. KeyMigrationColumnsTests (Server.Tests)
// vergleicht die Liste mit dem Modell und scheitert, wenn eine Spalte fehlt oder der
// Purpose nicht passt.
internal static class EncryptedColumns
{
    public static IReadOnlyList<EncryptedColumn> All { get; } =
    [
        // Nachsichtig wie EncryptedConverter.Lenient: Der Refresh-Token-Altbestand kann noch
        // Klartext sein; beim Umschlüsseln wird er dabei gleich mitverschlüsselt.
        new("GoogleConnections", "RefreshToken", "HorizonNET.GoogleConnection.RefreshToken", Strict: false),
        // Nachsichtig wie im Modell: Nicht entschlüsselbar heißt nur "neu verbinden".
        new("CalDavConnections", "AppPassword",  "HorizonNET.CalDav.Password",               Strict: false),
        // Nachsichtig wie im Modell: Ein unlesbarer PAT heißt nur "neu verbinden". Fehlte hier
        // bis 1.3.0, beim Umzug des Schlüsselrings wäre der PAT unlesbar geworden.
        new("AzureDevOpsConnections", "PersonalAccessToken", "HorizonNET.AzureDevOps.Pat",   Strict: false),
        new("JournalEntries",   "Content",      "HorizonNET.Journal.Content",               Strict: true),
        new("JournalEntries",    "Title",        "HorizonNET.Journal.Title",                 Strict: true),
        new("MoodEntries",       "Note",         "HorizonNET.Journal.MoodNote",              Strict: true),
    ];
}

// Strict spiegelt EncryptedConverter.Strict/Lenient: streng = Abbruch bei unlesbarem Wert.
internal sealed record EncryptedColumn(string Table, string Column, string Purpose, bool Strict);
