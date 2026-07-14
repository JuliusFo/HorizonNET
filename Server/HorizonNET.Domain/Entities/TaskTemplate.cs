using HorizonNET.Shared.Transfer.Enums;

namespace HorizonNET.Domain.Entities;

// Vorlage für wiederkehrende Aufgaben (z. B. "Bereitschaftsanruf"): belegt beim
// Anlegen eines Tasks Titel, Beschreibung, Priorität und optional das Projekt vor.
// Erzeugt selbst keine Tasks – sie wird beim Anlegen nur einmalig angewendet.
public class TaskTemplate
{
    public int Id { get; set; }

    // Zugleich der vorbelegte Task-Titel und die Beschriftung in der Vorlagen-Auswahl.
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public Priority Priority { get; set; } = Priority.Medium;

    // Optionale Projektzuordnung; beim Löschen des Projekts bleibt die Vorlage erhalten.
    public int? ProjectId { get; set; }

    public Project? Project { get; set; }

    // Manuelle Reihenfolge in der Verwaltung und in den Auswahllisten.
    public int SortOrder { get; set; }

    // Soft-Delete: null = aktiv (siehe TaskItem.DeletedAt).
    public DateTime? DeletedAt { get; set; }
}
