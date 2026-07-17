using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HorizonNET.Data.Repositories;

public class ExerciseSetRepository(AppDbContext context) : IExerciseSetRepository
{
    public async Task<IEnumerable<ExerciseSet>> GetAsync(DateTime? from, DateTime? to, int? exerciseId)
    {
        var query = context.ExerciseSets.Include(s => s.Exercise).AsQueryable();

        if (from is not null) query = query.Where(s => s.PerformedAt >= from);
        if (to is not null) query = query.Where(s => s.PerformedAt < to);
        if (exerciseId is not null) query = query.Where(s => s.ExerciseId == exerciseId);

        return await query
            .OrderBy(s => s.PerformedAt)
            .ThenBy(s => s.SetNumber)
            .ToListAsync();
    }

    public async Task<ExerciseSet?> GetByIdAsync(int id) =>
        await context.ExerciseSets.Include(s => s.Exercise).FirstOrDefaultAsync(s => s.Id == id);

    public async Task<ExerciseSet> CreateAsync(ExerciseSet set)
    {
        set.CreatedAt = DateTime.Now;

        // Satznummer fortlaufend je Übung und Tag. Serverseitig, damit sie unabhängig
        // vom Aufrufer stimmt und beim Nachtragen älterer Einheiten nicht kollidiert.
        var day = set.PerformedAt.Date;
        var maxNumber = await context.ExerciseSets
            .IgnoreQueryFilters()
            .Where(s => s.ExerciseId == set.ExerciseId
                     && s.PerformedAt >= day
                     && s.PerformedAt < day.AddDays(1))
            .MaxAsync(s => (int?)s.SetNumber) ?? 0;
        set.SetNumber = maxNumber + 1;

        context.ExerciseSets.Add(set);
        await context.SaveChangesAsync();
        return await GetByIdAsync(set.Id) ?? set;
    }

    public async Task<ExerciseSet?> UpdateAsync(int id, ExerciseSet updated)
    {
        var existing = await context.ExerciseSets.FirstOrDefaultAsync(s => s.Id == id);
        if (existing is null) return null;

        existing.PerformedAt = updated.PerformedAt;
        existing.Reps = updated.Reps;
        existing.WeightKg = updated.WeightKg;
        existing.DistanceMeters = updated.DistanceMeters;
        existing.DurationSeconds = updated.DurationSeconds;
        existing.Rpe = updated.Rpe;
        existing.Notes = updated.Notes;

        // ExerciseId und SetNumber bleiben: Ein Satz wechselt nicht die Übung, und die
        // Nummer vergibt der Server.

        await context.SaveChangesAsync();
        return await GetByIdAsync(id);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var existing = await context.ExerciseSets.FirstOrDefaultAsync(s => s.Id == id);
        if (existing is null) return false;

        existing.DeletedAt = DateTime.Now;
        await context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RestoreAsync(int id)
    {
        var existing = await context.ExerciseSets
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == id);
        if (existing is null || existing.DeletedAt is null) return false;

        existing.DeletedAt = null;
        await context.SaveChangesAsync();
        return true;
    }
}
