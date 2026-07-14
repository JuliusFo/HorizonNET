using HorizonNET.Domain.Entities;

namespace HorizonNET.Domain.Interfaces;

public interface IProjectRepository
{
    Task<IEnumerable<Project>> GetAllAsync();

    Task<Project?> GetByIdAsync(int id);

    Task<Project> CreateAsync(Project project);

    Task<Project?> UpdateAsync(int id, Project project);

    Task<bool> DeleteAsync(int id);

    // Macht ein Soft-Delete rückgängig (Projekt + im selben Vorgang gelöschte Tasks).
    Task<bool> RestoreAsync(int id);

    // Globale Suche über Name und Beschreibung (für die Kommandopalette).
    Task<IEnumerable<Project>> SearchAsync(string query, int limit);
}
