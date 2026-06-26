using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HorizonNET.Data.Repositories;

public class GoogleConnectionRepository(AppDbContext context) : IGoogleConnectionRepository
{
    // Single-Row-Tabelle: OrderBy nur, damit EF nicht vor unbestimmter Reihenfolge warnt.
    public async Task<GoogleConnection?> GetAsync() =>
        await context.GoogleConnections.OrderBy(c => c.Id).FirstOrDefaultAsync();

    public async Task<GoogleConnection> SaveAsync(GoogleConnection connection)
    {
        var existing = await context.GoogleConnections.OrderBy(c => c.Id).FirstOrDefaultAsync();
        if (existing is null)
        {
            context.GoogleConnections.Add(connection);
        }
        else
        {
            existing.RefreshToken = connection.RefreshToken;
            existing.Email = connection.Email;
            existing.ConnectedAtUtc = connection.ConnectedAtUtc;
            connection = existing;
        }

        await context.SaveChangesAsync();
        return connection;
    }

    public async Task<bool> DeleteAsync()
    {
        var existing = await context.GoogleConnections.OrderBy(c => c.Id).FirstOrDefaultAsync();
        if (existing is null) return false;

        context.GoogleConnections.Remove(existing);
        await context.SaveChangesAsync();
        return true;
    }
}
