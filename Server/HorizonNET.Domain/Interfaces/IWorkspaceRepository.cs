using HorizonNET.Domain.Entities;

namespace HorizonNET.Domain.Interfaces;

public interface IWorkspaceRepository
{
    Task<IEnumerable<Workspace>> GetAllAsync();

    Task<Workspace?> GetByIdAsync(int id);

    Task<Workspace> CreateAsync(Workspace workspace);

    Task<Workspace?> UpdateAsync(int id, Workspace workspace);

    Task<bool> DeleteAsync(int id);

    Task<bool> RestoreAsync(int id);

    // Soft-gelöschte Arbeitsbereiche (für den Papierkorb), zuletzt gelöscht zuerst.
    Task<IEnumerable<Workspace>> GetDeletedAsync();

    // Endgültiges Löschen eines soft-gelöschten Arbeitsbereichs (nicht umkehrbar).
    Task<bool> PurgeAsync(int id);
}
