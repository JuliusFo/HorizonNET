using HorizonNET.Domain.Entities;

namespace HorizonNET.Domain.Interfaces;

public interface INoteFolderRepository
{
    Task<IEnumerable<NoteFolder>> GetAllAsync();

    Task<NoteFolder?> GetByIdAsync(int id);

    Task<NoteFolder> CreateAsync(NoteFolder folder);

    // Umbenennen. Das Verschieben läuft bewusst über MoveAsync – dort hängt die
    // Zyklusprüfung dran, die beim reinen Umbenennen nur im Weg stünde.
    Task<NoteFolder?> RenameAsync(int id, string name);

    /// <summary>
    /// Hängt den Ordner unter einen anderen (null = oberste Ebene).
    /// Gibt null zurück, wenn es den Ordner nicht gibt ODER das Ziel ein Nachfahre wäre
    /// (das würde den Teilbaum von der Wurzel abschneiden).
    /// </summary>
    Task<NoteFolder?> MoveAsync(int id, int? newParentId);

    // Soft-Delete inkl. Unterordner (gemeinsamer Zeitstempel für Undo). Die Notizen
    // darin bleiben erhalten und behalten ihre Zuordnung.
    Task<bool> DeleteAsync(int id);

    Task<bool> RestoreAsync(int id);

    // Soft-gelöschte Ordner für den Papierkorb. Nur die „Wurzeln" eines Löschvorgangs:
    // Unterordner, die im selben Vorgang mitgingen, kämen beim Wiederherstellen der
    // Wurzel automatisch mit und würden die Liste sonst nur aufblähen.
    Task<IEnumerable<NoteFolder>> GetDeletedAsync();

    // Endgültiges Löschen inkl. Unterordner (nicht umkehrbar). Notizen darin bleiben
    // erhalten; ihre Zuordnung räumt der Fremdschlüssel per SetNull ab.
    Task<bool> PurgeAsync(int id);
}
