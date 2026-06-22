using HorizonNET.Domain.Entities;

namespace HorizonNET.Domain.Interfaces;

public interface ITaskRepository
{
    Task<IEnumerable<TaskItem>> GetAllAsync();

    Task<IEnumerable<TaskItem>> GetByProjectIdAsync(int projectId);

    Task<TaskItem?> GetByIdAsync(int id);

    Task<TaskItem> CreateAsync(TaskItem task);

    Task<TaskItem?> UpdateAsync(int id, TaskItem task);

    Task<bool> DeleteAsync(int id);
}