using HorizonNET.Domain.Entities;

namespace HorizonNET.Domain.Interfaces;

public interface IDailyTaskRepository
{
    // Alle Dailies (für die Verwaltung), inkl. Completions. Nach SortOrder.
    Task<IEnumerable<DailyTask>> GetAllAsync();

    // Nur aktive Dailies (für die Heute-Liste), inkl. Completions. Nach SortOrder.
    Task<IEnumerable<DailyTask>> GetActiveAsync();

    Task<DailyTask?> GetByIdAsync(int id);

    Task<DailyTask> CreateAsync(DailyTask task);

    Task<DailyTask?> UpdateAsync(int id, DailyTask task);

    Task<bool> DeleteAsync(int id);

    Task<bool> RestoreAsync(int id);

    Task ReorderAsync(IList<int> orderedIds);

    // Setzt/entfernt das Häkchen für einen Tag (idempotent). false, wenn Daily nicht existiert.
    Task<bool> SetCompletionAsync(int dailyTaskId, DateOnly date, bool completed);

    // Soft-gelöschte Dailies (für den Papierkorb), zuletzt gelöscht zuerst.
    Task<IEnumerable<DailyTask>> GetDeletedAsync();

    // Endgültiges Löschen eines soft-gelöschten Dailys inkl. Häkchen (nicht umkehrbar).
    Task<bool> PurgeAsync(int id);
}
