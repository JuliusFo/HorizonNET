using HorizonNET.Shared.Transfer.Enums;

namespace HorizonNET.Domain.Entities;

// Terminserie: "Volleyball – Mo 19:00–21:00, Mi 18:30–20:00, Fr 20:00–22:00". Aus der Serie
// werden echte TaskItems MATERIALISIERT (Paket f, Generator), je Slot und Datum eines. Der
// Einzeltermin bleibt damit eine normale Task-Zeile: verschiebbar, einzeln absagbar, mit
// Notizen und Google-Sync – die Serie ist nur die Schablone.
//
// Bewusst getrennt von DailyTask: Der erzeugt keine Tasks, hat keine Uhrzeiten und taucht
// nie im Kalender auf.
public class TaskSeries
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    // Aufgabe oder Termin – die materialisierten Tasks erben es. Termin ist der Normalfall;
    // "Aufgabe" hält den Weg für wiederkehrende Arbeits-Tasks offen (Feature-Idee #2).
    public TaskKind Kind { get; set; } = TaskKind.Appointment;

    public Priority Priority { get; set; } = Priority.Medium;

    // Erinnerung, die jeder materialisierte Termin erbt (null = Standard aus den
    // Einstellungen, TaskReminder.None = keine). Siehe TaskReminder.
    public int? ReminderMinutes { get; set; }

    // Pausieren statt löschen: Der Generator legt für inaktive Serien nichts Neues an;
    // bereits materialisierte Termine bleiben unberührt.
    public bool IsActive { get; set; } = true;

    // Optionale Projektzuordnung (SetNull beim Löschen des Projekts); die materialisierten
    // Termine tragen dieselbe ProjectId.
    public int? ProjectId { get; set; }

    public Project? Project { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    // Soft-Delete: null = aktiv. Was mit den materialisierten Terminen passiert, regelt der
    // Generator-Teil (Paket f): zukünftige mit-löschen, vergangene als Historie behalten.
    public DateTime? DeletedAt { get; set; }

    // Wochentag/Uhrzeit-Slots – mehrere pro Wochentag erlaubt.
    public ICollection<TaskSeriesSlot> Slots { get; set; } = [];

    // Aus dieser Serie materialisierte Termine.
    public ICollection<TaskItem> Tasks { get; set; } = [];
}
