using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HorizonNET.Data.Repositories;

public class AppSettingRepository(AppDbContext context) : IAppSettingRepository
{
    public async Task<string?> GetAsync(string key) =>
        (await context.AppSettings.FirstOrDefaultAsync(s => s.Key == key))?.Value;

    public async Task SetAsync(string key, string value)
    {
        var existing = await context.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (existing is null)
            context.AppSettings.Add(new AppSetting { Key = key, Value = value });
        else
            existing.Value = value;

        await context.SaveChangesAsync();
    }
}
