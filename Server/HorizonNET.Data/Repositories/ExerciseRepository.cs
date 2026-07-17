using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HorizonNET.Data.Repositories;

public class ExerciseRepository(AppDbContext context) : IExerciseRepository
{
    public async Task<IEnumerable<Exercise>> GetAllAsync() =>
        await context.Exercises.OrderBy(x => x.SortOrder).ToListAsync();

    public async Task<Exercise?> GetByIdAsync(int id) =>
        await context.Exercises.FirstOrDefaultAsync(x => x.Id == id);

    public async Task<Exercise> CreateAsync(Exercise exercise)
    {
        exercise.CreatedAt = DateTime.Now;

        // Neue Übungen ans Ende. IgnoreQueryFilters: auch gelöschte zählen mit, sonst
        // bekäme eine wiederhergestellte Übung denselben Wert wie eine neue.
        var maxOrder = await context.Exercises
            .IgnoreQueryFilters()
            .MaxAsync(x => (int?)x.SortOrder) ?? -1;
        exercise.SortOrder = maxOrder + 1;

        context.Exercises.Add(exercise);
        await context.SaveChangesAsync();
        return exercise;
    }

    public async Task<Exercise?> UpdateAsync(int id, Exercise updated)
    {
        var existing = await context.Exercises.FirstOrDefaultAsync(x => x.Id == id);
        if (existing is null) return null;

        existing.Name = updated.Name;
        existing.Notes = updated.Notes;
        existing.IsActive = updated.IsActive;

        // Kind bleibt bewusst unangetastet: Die Art bestimmt, welche Felder der schon
        // erfassten Sätze belegt sind. Ein nachträglicher Wechsel würde die Historie
        // sinnlos machen (Laufeinträge ohne Wiederholungen als Kraftübung lesen).
        // Wer die Art ändern will, legt eine neue Übung an.

        await context.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var existing = await context.Exercises.FirstOrDefaultAsync(x => x.Id == id);
        if (existing is null) return false;

        // Sätze mit demselben Zeitstempel stempeln – der gruppiert den Vorgang, damit
        // Undo genau die Sätze zurückholt, die mit dieser Übung verschwunden sind
        // (Muster wie Projekt → Tasks).
        var now = DateTime.Now;
        existing.DeletedAt = now;

        var sets = await context.ExerciseSets
            .IgnoreQueryFilters()
            .Where(s => s.ExerciseId == id && s.DeletedAt == null)
            .ToListAsync();
        foreach (var s in sets) s.DeletedAt = now;

        await context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RestoreAsync(int id)
    {
        var existing = await context.Exercises
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == id);
        if (existing is null || existing.DeletedAt is null) return false;

        var deletedAt = existing.DeletedAt.Value;
        existing.DeletedAt = null;

        // Nur die Sätze aus demselben Löschvorgang zurückholen – vorher einzeln
        // gelöschte Sätze bleiben gelöscht.
        var sets = await context.ExerciseSets
            .IgnoreQueryFilters()
            .Where(s => s.ExerciseId == id && s.DeletedAt == deletedAt)
            .ToListAsync();
        foreach (var s in sets) s.DeletedAt = null;

        await context.SaveChangesAsync();
        return true;
    }

    public async Task ReorderAsync(IList<int> orderedIds)
    {
        var exercises = await context.Exercises
            .Where(x => orderedIds.Contains(x.Id))
            .ToListAsync();

        foreach (var x in exercises)
            x.SortOrder = orderedIds.IndexOf(x.Id);

        await context.SaveChangesAsync();
    }
}
