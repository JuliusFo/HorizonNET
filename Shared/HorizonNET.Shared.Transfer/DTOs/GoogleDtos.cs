namespace HorizonNET.Shared.Transfer.DTOs;

// Status der Google-Verbindung für die Einstellungsseite.
public record GoogleStatusDto(
    bool Connected,
    string? Email
);

// Vorlaufzeit der Erinnerung am gespiegelten Google-Termin.
// null = keine Erinnerung. Erledigte Tasks bekommen unabhängig davon nie eine.
public record GoogleReminderDto(int? Minutes);

// Ein Termin aus dem Google-Kalender (read-only-Anzeige im Kalender).
public record GoogleEventDto(
    string Id,
    string Title,
    DateTime Start,
    DateTime End,
    bool AllDay
);
