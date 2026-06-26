using HorizonNET.Domain.Entities;

namespace HorizonNET.Domain.Interfaces;

public interface IGoogleConnectionRepository
{
    // Die (höchstens eine) gespeicherte Verbindung, oder null wenn nicht verbunden.
    Task<GoogleConnection?> GetAsync();

    // Legt die Verbindung an oder aktualisiert die bestehende (Upsert).
    Task<GoogleConnection> SaveAsync(GoogleConnection connection);

    // Entfernt die Verbindung (trennen). Liefert false, wenn keine bestand.
    Task<bool> DeleteAsync();
}
