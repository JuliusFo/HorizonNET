using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HorizonNET.Data.Repositories;

public class BodyWeightRepository(AppDbContext context) : IBodyWeightRepository
{
    public async Task<IEnumerable<BodyWeightEntry>> GetAsync(DateOnly? from, DateOnly? to)
    {
        var query = context.BodyWeightEntries.AsQueryable();

        if (from is not null) query = query.Where(b => b.MeasuredOn >= from);
        if (to is not null) query = query.Where(b => b.MeasuredOn <= to);

        return await query.OrderBy(b => b.MeasuredOn).ToListAsync();
    }

    public async Task<BodyWeightEntry> SetAsync(DateOnly measuredOn, double weightKg)
    {
        // Höchstens ein Eintrag pro Tag (eindeutiger Index). Ein zweiter Wert für
        // denselben Tag ist eine Korrektur, kein neuer Datenpunkt – also überschreiben
        // statt den Index verletzen zu lassen.
        var existing = await context.BodyWeightEntries
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.MeasuredOn == measuredOn);

        if (existing is not null)
        {
            existing.WeightKg = weightKg;
            existing.DeletedAt = null;   // ein zuvor gelöschter Tag wird wieder aktiv
            await context.SaveChangesAsync();
            return existing;
        }

        var entry = new BodyWeightEntry
        {
            MeasuredOn = measuredOn,
            WeightKg = weightKg,
            CreatedAt = DateTime.Now
        };

        context.BodyWeightEntries.Add(entry);
        await context.SaveChangesAsync();
        return entry;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var existing = await context.BodyWeightEntries.FirstOrDefaultAsync(b => b.Id == id);
        if (existing is null) return false;

        existing.DeletedAt = DateTime.Now;
        await context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RestoreAsync(int id)
    {
        var existing = await context.BodyWeightEntries
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.Id == id);
        if (existing is null || existing.DeletedAt is null) return false;

        existing.DeletedAt = null;
        await context.SaveChangesAsync();
        return true;
    }
}
