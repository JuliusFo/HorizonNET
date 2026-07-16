using HorizonNET.Domain.Entities;
using HorizonNET.Shared.Transfer.Enums;

namespace HorizonNET.Domain.Interfaces;

public interface ITaskRepository
{
    Task<IEnumerable<TaskItem>> GetAllAsync();

    Task<IEnumerable<TaskItem>> GetByProjectIdAsync(int projectId);

    Task<IEnumerable<TaskItem>> GetInboxAsync();

    Task<TaskItem?> GetByIdAsync(int id);

    Task<TaskItem> CreateAsync(TaskItem task);

    // Vollersatz aller Felder – nur für die echten Editoren (Detailseite, Dialog).
    Task<TaskItem?> UpdateAsync(int id, TaskItem task);

    // Zweckgebundene Teil-Updates: ändern genau ein Anliegen und lassen den Rest in
    // Ruhe. Wer nur abhakt oder verschiebt, kann so nichts überschreiben, was er gar
    // nicht kennt (und nicht mit einem veralteten Stand zurückrollen).
    Task<TaskItem?> SetStatusAsync(int id, WorkStatus status);

    Task<TaskItem?> SetScheduleAsync(int id, DateTime? dueDate, DateTime? startTime, DateTime? endTime);

    Task<TaskItem?> SetProjectAsync(int id, int? projectId);

    // Setzt nur die Google-Event-Verknüpfung (serverseitiger Sync). Bewusst getrennt
    // vom DTO-getriebenen UpdateAsync, damit dieser Wert dort nicht überschrieben wird.
    Task SetGoogleEventIdAsync(int taskId, string? googleEventId);

    // Alle gesetzten Google-Event-Ids – um beim Lesen die von der App selbst
    // gespiegelten Termine auszublenden (sonst Doppelanzeige Task + Google-Event).
    Task<HashSet<string>> GetGoogleEventIdsAsync();

    // Setzt für die übergebenen Tasks SortOrder = Listenindex und Status = status.
    Task ReorderAsync(WorkStatus status, IList<int> orderedTaskIds);

    // Setzt nur SortOrder = Listenindex (Status bleibt unverändert) – für Sub-Tasks.
    Task ReorderSubTasksAsync(IList<int> orderedTaskIds);

    // Setzt ListSortOrder = Listenindex – die manuelle Reihenfolge der Haupt-Tasks in
    // der Projektliste. Rührt weder Status noch SortOrder (Kanban) an.
    Task ReorderTaskListAsync(IList<int> orderedTaskIds);

    // Soft-Delete: stempelt den Task (und aktive Sub-Tasks) als gelöscht.
    Task<bool> DeleteAsync(int id);

    // Macht ein Soft-Delete rückgängig (Task + im selben Vorgang gelöschte Sub-Tasks).
    Task<bool> RestoreAsync(int id);

    // Globale Suche über Titel und Beschreibung (für die Kommandopalette).
    Task<IEnumerable<TaskItem>> SearchAsync(string query, int limit);

    // Soft-gelöschte Tasks (für den Papierkorb), aber nur eigenständig gelöschte
    // "Wurzeln": Tasks, die als Teil desselben Vorgangs mit ihrem Projekt oder
    // Eltern-Task gelöscht wurden, kommen dort automatisch mit zurück und tauchen
    // deshalb nicht separat auf. Zuletzt gelöscht zuerst.
    Task<IEnumerable<TaskItem>> GetDeletedAsync();

    // Endgültiges Löschen eines soft-gelöschten Tasks inkl. Sub-Tasks (nicht umkehrbar).
    Task<bool> PurgeAsync(int id);
}
