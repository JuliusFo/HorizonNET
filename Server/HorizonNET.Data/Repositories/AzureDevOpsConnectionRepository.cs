using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HorizonNET.Data.Repositories;

// Single-Row-Tabelle, Muster wie GoogleConnectionRepository.
public class AzureDevOpsConnectionRepository(AppDbContext context) : IAzureDevOpsConnectionRepository
{
    public async Task<AzureDevOpsConnection?> GetAsync()
    {
        var existing = await context.AzureDevOpsConnections.OrderBy(c => c.Id).FirstOrDefaultAsync();

        // Leerer PAT = nicht entschlüsselbar (anderer Schlüsselring) – wie "nicht verbunden".
        return string.IsNullOrEmpty(existing?.PersonalAccessToken) ? null : existing;
    }

    public async Task<AzureDevOpsConnection> SaveAsync(AzureDevOpsConnection connection)
    {
        var existing = await context.AzureDevOpsConnections.OrderBy(c => c.Id).FirstOrDefaultAsync();
        if (existing is null)
        {
            context.AzureDevOpsConnections.Add(connection);
        }
        else
        {
            existing.OrganizationUrl = connection.OrganizationUrl;
            existing.PersonalAccessToken = connection.PersonalAccessToken;
            existing.DisplayName = connection.DisplayName;
            existing.ConnectedAtUtc = connection.ConnectedAtUtc;
            connection = existing;
        }

        await context.SaveChangesAsync();
        return connection;
    }

    public async Task<bool> DeleteAsync()
    {
        var existing = await context.AzureDevOpsConnections.OrderBy(c => c.Id).FirstOrDefaultAsync();
        if (existing is null) return false;

        context.AzureDevOpsConnections.Remove(existing);
        await context.SaveChangesAsync();
        return true;
    }
}
