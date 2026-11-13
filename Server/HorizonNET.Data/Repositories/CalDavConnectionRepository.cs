using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HorizonNET.Data.Repositories;

// Single-Row-Tabelle, Muster wie GoogleConnectionRepository.
public class CalDavConnectionRepository(AppDbContext context) : ICalDavConnectionRepository
{
    public async Task<CalDavConnection?> GetAsync()
    {
        var existing = await context.CalDavConnections.OrderBy(c => c.Id).FirstOrDefaultAsync();

        // Leeres Passwort = nicht entschlüsselbar (anderer Schlüsselring) – wie "nicht verbunden".
        return string.IsNullOrEmpty(existing?.AppPassword) ? null : existing;
    }

    public async Task<CalDavConnection> SaveAsync(CalDavConnection connection)
    {
        var existing = await context.CalDavConnections.OrderBy(c => c.Id).FirstOrDefaultAsync();
        if (existing is null)
        {
            context.CalDavConnections.Add(connection);
        }
        else
        {
            existing.ServerUrl = connection.ServerUrl;
            existing.Username = connection.Username;
            existing.AppPassword = connection.AppPassword;
            existing.CalendarUrl = connection.CalendarUrl;
            existing.CalendarName = connection.CalendarName;
            existing.ConnectedAtUtc = connection.ConnectedAtUtc;
            connection = existing;
        }

        await context.SaveChangesAsync();
        return connection;
    }

    public async Task<bool> DeleteAsync()
    {
        var existing = await context.CalDavConnections.OrderBy(c => c.Id).FirstOrDefaultAsync();
        if (existing is null) return false;

        context.CalDavConnections.Remove(existing);
        await context.SaveChangesAsync();
        return true;
    }
}
