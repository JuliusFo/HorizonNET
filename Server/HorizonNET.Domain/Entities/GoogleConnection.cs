namespace HorizonNET.Domain.Entities;

// Speichert die OAuth-Verbindung zum Google-Konto. Da die App Single-User ist,
// gibt es höchstens eine Zeile. Nur der langlebige Refresh-Token wird persistiert;
// Access-Tokens werden bei Bedarf daraus neu beschafft.
public class GoogleConnection
{
    public int Id { get; set; }

    public string RefreshToken { get; set; } = string.Empty;

    // E-Mail des verbundenen Kontos (für die Anzeige „Verbunden als …").
    public string? Email { get; set; }

    public DateTime ConnectedAtUtc { get; set; }
}
