using HorizonNET.Domain.Entities;

namespace HorizonNET.Domain.Interfaces;

public interface IJournalTemplateRepository
{
    Task<IEnumerable<JournalTemplate>> GetAllAsync();

    Task<JournalTemplate?> GetByIdAsync(int id);

    Task<JournalTemplate> CreateAsync(JournalTemplate template);

    Task<JournalTemplate?> UpdateAsync(int id, JournalTemplate template);

    Task<bool> DeleteAsync(int id);
}
