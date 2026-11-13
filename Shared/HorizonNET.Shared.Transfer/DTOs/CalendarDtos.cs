namespace HorizonNET.Shared.Transfer.DTOs;

// Anbieter-Schlüssel im Status. Es ist immer höchstens einer verbunden.
public static class CalendarProviders
{
    public const string Google = "google";
    public const string Nextcloud = "nextcloud";
}

// Status der Kalender-Verbindung für die Einstellungsseite.
// Provider = null → nicht verbunden. Account = E-Mail (Google) bzw. Benutzername (Nextcloud).
// GoogleAvailable: Sind Google-Credentials auf dem Server hinterlegt? Sonst bietet die
// Einstellungsseite nur Nextcloud an.
public record CalendarStatusDto(
    string? Provider,
    string? Account,
    string? CalendarName,
    bool GoogleAvailable = true
)
{
    public bool Connected => Provider is not null;
}

// Vorlaufzeit der Erinnerung am gespiegelten Termin.
// null = keine Erinnerung. Erledigte Tasks bekommen unabhängig davon nie eine.
public record CalendarReminderDto(int? Minutes);

// Ein Termin aus dem verbundenen Kalender (read-only-Anzeige im Kalender).
public record CalendarEventDto(
    string Id,
    string Title,
    DateTime Start,
    DateTime End,
    bool AllDay
);

// ── Nextcloud / CalDAV ──────────────────────────────────────────────────────────

// Zugangsdaten für die Kalendersuche. App-Passwort aus Nextcloud → Einstellungen → Sicherheit.
public record CalDavCredentialsDto(
    string ServerUrl,
    string Username,
    string AppPassword
);

// Ein gefundener Kalender (absolute Collection-URL + Anzeigename).
public record CalDavCalendarDto(string Url, string Name);

// Ergebnis der Kalendersuche. Fehler kommen als Text, nie als 401 (siehe CalDavController).
public record CalDavDiscoverResultDto(
    bool Success,
    string? Error,
    List<CalDavCalendarDto> Calendars
);

public record CalDavConnectDto(
    string ServerUrl,
    string Username,
    string AppPassword,
    string CalendarUrl
);

public record CalDavConnectResultDto(
    bool Success,
    string? Error,
    CalendarStatusDto? Status
);
