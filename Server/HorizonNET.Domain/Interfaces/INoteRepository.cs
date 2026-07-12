using HorizonNET.Domain.Entities;

namespace HorizonNET.Domain.Interfaces;

public interface INoteRepository
{
    Task<IEnumerable<Note>> GetAllAsync();

    Task<Note?> GetByIdAsync(int id);

    Task<IEnumerable<Note>> GetByTaskIdAsync(int taskId);

    Task<IEnumerable<Note>> GetByProjectIdAsync(int projectId);

    Task<Note> CreateAsync(Note note);

    Task<Note?> UpdateAsync(int id, Note note);

    Task<bool> DeleteAsync(int id);
}
