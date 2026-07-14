using HorizonNET.Domain.Entities;

namespace HorizonNET.Domain.Interfaces;

public interface ITimeEntryRepository
{
    // Alle Intervalle eines Tasks, neueste zuerst.
    Task<IEnumerable<TimeEntry>> GetByTaskAsync(int taskId);

    // Das systemweit einzige laufende Intervall (EndedAt == null), falls vorhanden.
    Task<TimeEntry?> GetRunningAsync();

    // Stoppt das laufende Intervall des Tasks. false, wenn keines läuft.
    // Gestartet wird ausschließlich über den Status (siehe TaskRepository), damit es
    // nur einen Pfad gibt, der die Kopplung Status ↔ Timer herstellt.
    Task<bool> StopAsync(int taskId);
}
