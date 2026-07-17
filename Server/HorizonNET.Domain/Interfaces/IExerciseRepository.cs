using HorizonNET.Domain.Entities;

namespace HorizonNET.Domain.Interfaces;

public interface IExerciseRepository
{
    // Alle nicht gelöschten Übungen (inkl. archivierter), nach SortOrder.
    Task<IEnumerable<Exercise>> GetAllAsync();

    Task<Exercise?> GetByIdAsync(int id);

    Task<Exercise> CreateAsync(Exercise exercise);

    Task<Exercise?> UpdateAsync(int id, Exercise exercise);

    // Soft-Delete: stempelt die Übung und ihre aktiven Sätze mit demselben Zeitstempel,
    // damit Undo den Vorgang als Ganzes zurückholt.
    Task<bool> DeleteAsync(int id);

    Task<bool> RestoreAsync(int id);

    Task ReorderAsync(IList<int> orderedIds);
}
