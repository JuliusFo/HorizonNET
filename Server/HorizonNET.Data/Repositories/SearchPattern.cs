namespace HorizonNET.Data.Repositories;

// Hilfen für die globale Suche. SQLite-LIKE ist bei ASCII case-insensitive;
// Wildcards (% _) in der Nutzereingabe müssen escaped werden, sonst würde
// z. B. "50%" alles finden.
internal static class SearchPattern
{
    public const string Escape = "\\";

    public static string For(string query) =>
        "%" + query.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
}
