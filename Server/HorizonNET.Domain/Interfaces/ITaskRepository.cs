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

    Task<TaskItem?> UpdateAsync(int id, TaskItem task);

    // Setzt für die übergebenen Tasks SortOrder = Listenindex und Status = status.
    Task ReorderAsync(WorkStatus status, IList<int> orderedTaskIds);

    // Setzt nur SortOrder = Listenindex (Status bleibt unverändert) – für Sub-Tasks.
    Task ReorderSubTasksAsync(IList<int> orderedTaskIds);

    Task<bool> DeleteAsync(int id);
}
