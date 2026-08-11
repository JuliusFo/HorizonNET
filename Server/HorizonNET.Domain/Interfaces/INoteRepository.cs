using HorizonNET.Domain.Entities;

namespace HorizonNET.Domain.Interfaces;

public interface INoteRepository
{
    Task<IEnumerable<Note>> GetAllAsync();

    Task<Note?> GetByIdAsync(int id);

    Task<IEnumerable<Note>> GetByTaskIdAsync(int taskId);

    Task<IEnumerable<Note>> GetByProjectIdAsync(int projectId);

    // Nur die DIREKT am Arbeitsbereich hängenden Notizen – nicht die seiner Projekte.
    // Die Detailseite listet darunter ohnehin die Projekte mit ihren eigenen Notizen.
    Task<IEnumerable<Note>> GetByWorkspaceIdAsync(int workspaceId);

    Task<Note> CreateAsync(Note note);

    Task<Note?> UpdateAsync(int id, Note note);

    Task<bool> DeleteAsync(int id);

    Task<bool> RestoreAsync(int id);

    // Globale Suche über Titel und Inhalt (für die Kommandopalette).
    Task<IEnumerable<Note>> SearchAsync(string query, int limit);

    // Soft-gelöschte Notizen (für den Papierkorb), zuletzt gelöscht zuerst.
    Task<IEnumerable<Note>> GetDeletedAsync();

    // Endgültiges Löschen einer soft-gelöschten Notiz (nicht umkehrbar).
    Task<bool> PurgeAsync(int id);
}
