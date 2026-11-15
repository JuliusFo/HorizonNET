using HorizonNET.Domain.Entities;

namespace HorizonNET.Domain.Interfaces;

public interface ICalDavConnectionRepository
{
    // null = nicht verbunden (auch bei nicht entschlüsselbarem Passwort).
    Task<CalDavConnection?> GetAsync();

    Task<CalDavConnection> SaveAsync(CalDavConnection connection);

    Task<bool> DeleteAsync();
}
