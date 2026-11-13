namespace HorizonNET.Domain.Entities;

// Verbindung zu einem CalDAV-Kalender (Nextcloud) per Benutzer + App-Passwort.
// Single-User-App: höchstens eine Zeile. Schließt sich mit GoogleConnection aus –
// es ist immer höchstens ein Kalender-Anbieter verbunden.
public class CalDavConnection
{
    public int Id { get; set; }

    // Basis-URL des Servers, wie eingegeben (normalisiert ohne abschließenden Slash),
    // z. B. https://cloud.example.org
    public string ServerUrl { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string AppPassword { get; set; } = string.Empty;

    // Absolute URL der Kalender-Collection, mit abschließendem Slash,
    // z. B. https://cloud.example.org/remote.php/dav/calendars/max/personal/
    public string CalendarUrl { get; set; } = string.Empty;

    // Anzeigename des gewählten Kalenders.
    public string? CalendarName { get; set; }

    public DateTime ConnectedAtUtc { get; set; }
}
