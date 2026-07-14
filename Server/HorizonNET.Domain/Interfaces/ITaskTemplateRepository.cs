using HorizonNET.Domain.Entities;

namespace HorizonNET.Domain.Interfaces;

public interface ITaskTemplateRepository
{
    // Alle Vorlagen inkl. Projekt, nach SortOrder.
    Task<IEnumerable<TaskTemplate>> GetAllAsync();

    Task<TaskTemplate?> GetByIdAsync(int id);

    Task<TaskTemplate> CreateAsync(TaskTemplate template);

    Task<TaskTemplate?> UpdateAsync(int id, TaskTemplate template);

    Task<bool> DeleteAsync(int id);

    Task<bool> RestoreAsync(int id);
}
