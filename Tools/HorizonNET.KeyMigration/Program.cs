using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;

// Schlüsselring-Umzug für die verschlüsselten Spalten (siehe docs/konzept-dpapi-umzug.md).
//
//   verify    --db <pfad> --keys <ring>
//   reprotect --db <pfad> --source-keys <ring> --target-keys <ring> [--target-dpapi]
//
// Die Spaltenliste und die Purpose-Strings MÜSSEN mit dem AppDbContext übereinstimmen –
// ein neuer verschlüsselter Wert im Modell gehört auch hier eingetragen.
var columns = new[]
{
    // Nachsichtig wie EncryptedConverter.Lenient: Der Refresh-Token-Altbestand kann noch
    // Klartext sein; beim Umschlüsseln wird er dabei gleich mitverschlüsselt.
    new EncryptedColumn("GoogleConnections", "RefreshToken", "HorizonNET.GoogleConnection.RefreshToken", Strict: false),
    new EncryptedColumn("JournalEntries",    "Content",      "HorizonNET.Journal.Content",               Strict: true),
    new EncryptedColumn("JournalEntries",    "Title",        "HorizonNET.Journal.Title",                 Strict: true),
    new EncryptedColumn("MoodEntries",       "Note",         "HorizonNET.Journal.MoodNote",              Strict: true),
};

try
{
    return args.FirstOrDefault() switch
    {
        "verify"    => Verify(Arg(args, "--db"), Arg(args, "--keys")),
        "reprotect" => Reprotect(Arg(args, "--db"), Arg(args, "--source-keys"), Arg(args, "--target-keys"),
                                 args.Contains("--target-dpapi")),
        _           => Usage()
    };
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    return Usage();
}

int Usage()
{
    Console.Error.WriteLine("""
        HorizonNET.KeyMigration – schlüsselt die verschlüsselten DB-Spalten um.

          verify    --db <pfad> --keys <ring>
          reprotect --db <pfad> --source-keys <ring> --target-keys <ring> [--target-dpapi]

        Ablauf und Warnungen: docs/konzept-dpapi-umzug.md
        """);
    return 2;
}

int Verify(string dbPath, string keysPath)
{
    using var db = Open(dbPath);
    var provider = CreateProvider(keysPath, dpapi: false);

    var strictFailures = 0;
    foreach (var col in columns)
    {
        var protector = provider.CreateProtector(col.Purpose);
        int ok = 0, plaintext = 0, failed = 0;

        foreach (var (_, stored) in ReadAll(db, col))
        {
            try
            {
                protector.Unprotect(stored);
                ok++;
            }
            catch (CryptographicException)
            {
                if (col.Strict) failed++;
                else plaintext++;
            }
        }

        if (col.Strict) strictFailures += failed;
        Console.WriteLine($"{col.Table}.{col.Column}: {ok} entschlüsselbar"
            + (plaintext > 0 ? $", {plaintext} Klartext-Altlast" : "")
            + (failed > 0 ? $", {failed} NICHT ENTSCHLÜSSELBAR" : ""));
    }

    Console.WriteLine(strictFailures == 0
        ? "OK – alle strengen Werte sind mit diesem Ring lesbar."
        : $"FEHLER – {strictFailures} Werte sind mit diesem Ring nicht lesbar. Falscher Ring?");
    return strictFailures == 0 ? 0 : 1;
}

int Reprotect(string dbPath, string sourceKeys, string targetKeys, bool targetDpapi)
{
    using var db = Open(dbPath);
    var source = CreateProvider(sourceKeys, dpapi: false);
    // dpapi wirkt nur auf neu ERZEUGTE Schlüssel – also nur fürs Ziel relevant.
    var target = CreateProvider(targetKeys, dpapi: targetDpapi);

    // Eine Transaktion über alles: Bricht irgendetwas ab, bleibt die DB unverändert –
    // ein halb umgeschlüsselter Bestand wäre mit KEINEM Ring mehr vollständig lesbar.
    using var tx = db.BeginTransaction();
    var total = 0;

    foreach (var col in columns)
    {
        var read  = source.CreateProtector(col.Purpose);
        var write = target.CreateProtector(col.Purpose);

        foreach (var (id, stored) in ReadAll(db, col).ToList())
        {
            string plain;
            try
            {
                plain = read.Unprotect(stored);
            }
            catch (CryptographicException) when (!col.Strict)
            {
                // Klartext-Altlast (Refresh-Token von vor Phase 14a) – ab jetzt verschlüsselt.
                plain = stored;
            }
            catch (CryptographicException)
            {
                Console.Error.WriteLine(
                    $"ABBRUCH: {col.Table}.{col.Column} (Id {id}) ist mit dem Quell-Ring nicht lesbar. " +
                    "Falscher --source-keys? Es wurde nichts geändert.");
                return 1;
            }

            using var update = db.CreateCommand();
            update.Transaction = tx;
            update.CommandText = $"UPDATE {col.Table} SET {col.Column} = $value WHERE Id = $id";
            update.Parameters.AddWithValue("$value", write.Protect(plain));
            update.Parameters.AddWithValue("$id", id);
            update.ExecuteNonQuery();
            total++;
        }
    }

    tx.Commit();
    Console.WriteLine($"Fertig – {total} Werte auf den Ziel-Ring umgeschlüsselt"
        + (targetDpapi ? " (DPAPI-gebunden an das ausführende Konto)." : " (Ziel-Ring OHNE DPAPI – nur für den Transport, danach löschen!)."));
    Console.WriteLine("Kontrolle: verify --db … --keys <ziel-ring>");
    return 0;
}

static SqliteConnection Open(string dbPath)
{
    if (!File.Exists(dbPath))
        throw new ArgumentException($"Datenbank nicht gefunden: {dbPath}");

    var db = new SqliteConnection($"Data Source={dbPath}");
    db.Open();
    return db;
}

static IDataProtectionProvider CreateProvider(string keysPath, bool dpapi) =>
    // Gleicher ApplicationName wie in der API (Program.cs) – er geht in die
    // Schlüsselableitung ein; ohne ihn passt kein einziges Chiffrat.
    DataProtectionProvider.Create(new DirectoryInfo(keysPath), options =>
    {
        options.SetApplicationName("HorizonNET");
        if (dpapi && OperatingSystem.IsWindows())
            options.ProtectKeysWithDpapi();
    });

static IEnumerable<(long Id, string Stored)> ReadAll(SqliteConnection db, EncryptedColumn col)
{
    using var cmd = db.CreateCommand();
    cmd.CommandText = $"SELECT Id, {col.Column} FROM {col.Table} WHERE {col.Column} IS NOT NULL";
    using var reader = cmd.ExecuteReader();
    while (reader.Read())
        yield return (reader.GetInt64(0), reader.GetString(1));
}

static string Arg(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    if (i < 0 || i + 1 >= args.Length)
        throw new ArgumentException($"Parameter {name} fehlt.");
    return args[i + 1];
}

// Strict spiegelt EncryptedConverter.Strict/Lenient: streng = Abbruch bei unlesbarem Wert.
internal sealed record EncryptedColumn(string Table, string Column, string Purpose, bool Strict);
