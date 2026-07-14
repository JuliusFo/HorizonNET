namespace HorizonNET.Shared.Transfer.DTOs;

// Ein einzelnes Zeit-Intervall eines Tasks. EndedAt = null → läuft gerade.
public record TimeEntryResponseDto(
    int Id,
    int TaskItemId,
    DateTime StartedAt,
    DateTime? EndedAt,
    int Seconds
);

// Der systemweit einzige laufende Timer (für die Anzeige in der Navigation).
public record RunningTimerDto(
    int TaskItemId,
    string TaskTitle,
    DateTime StartedAt
);
