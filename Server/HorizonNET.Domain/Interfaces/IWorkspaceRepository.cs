using HorizonNET.Domain.Entities;

namespace HorizonNET.Domain.Interfaces;

public interface IWorkspaceRepository
{
    Task<IEnumerable<Workspace>> GetAllAsync();

    Task<Workspace?> GetByIdAsync(int id);

    Task<Workspace> CreateAsync(Workspace workspace);

    Task<Workspace?> UpdateAsync(int id, Workspace workspace);

    Task<bool> DeleteAsync(int id);
}
