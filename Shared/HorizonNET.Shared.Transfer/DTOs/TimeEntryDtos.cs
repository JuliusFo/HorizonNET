namespace HorizonNET.Shared.Transfer.DTOs;

// Ein einzelnes Zeit-Intervall eines Tasks. EndedAt = null → läuft gerade.
public record TimeEntryResponseDto(
    int Id,
    int TaskItemId,
    DateTime StartedAt,
    DateTime? EndedAt,
    int Seconds
);

// Der systemweit einzige laufende Timer (für die Anzeige in der Kopfzeile).
public record RunningTimerDto(
    int TaskItemId,
    string TaskTitle,
    DateTime StartedAt,
    // Bereits ABGESCHLOSSENE Intervalle dieses Tasks. Das laufende fehlt bewusst –
    // die Anzeige zählt es aus StartedAt selbst weiter, sonst müsste sie sekündlich
    // nachfragen. Gesamtzeit = TrackedSeconds + (jetzt − StartedAt).
    int TrackedSeconds = 0
);
