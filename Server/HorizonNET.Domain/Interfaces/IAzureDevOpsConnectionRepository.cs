using HorizonNET.Domain.Entities;

namespace HorizonNET.Domain.Interfaces;

public interface IAzureDevOpsConnectionRepository
{
    // null = nicht verbunden (auch bei nicht entschlüsselbarem PAT).
    Task<AzureDevOpsConnection?> GetAsync();

    Task<AzureDevOpsConnection> SaveAsync(AzureDevOpsConnection connection);

    Task<bool> DeleteAsync();
}
