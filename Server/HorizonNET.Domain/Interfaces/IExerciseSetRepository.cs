using HorizonNET.Domain.Entities;

namespace HorizonNET.Domain.Interfaces;

public interface IExerciseSetRepository
{
    // Sätze in einem Zeitraum (für Historie und Auswertung), optional auf eine Übung
    // eingeschränkt. Chronologisch, inkl. Übung.
    Task<IEnumerable<ExerciseSet>> GetAsync(DateTime? from, DateTime? to, int? exerciseId);

    Task<ExerciseSet?> GetByIdAsync(int id);

    Task<ExerciseSet> CreateAsync(ExerciseSet set);

    Task<ExerciseSet?> UpdateAsync(int id, ExerciseSet set);

    Task<bool> DeleteAsync(int id);

    Task<bool> RestoreAsync(int id);
}
