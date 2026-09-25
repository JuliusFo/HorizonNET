namespace HorizonNET.Shared.Transfer.Enums;

// Aufgabe oder Termin? Beides ist ein TaskItem, aber nur Aufgaben gehören aufs Kanban-Board
// und in die Ungeplant-Leiste; Termine (Volleyball, Arzt) leben nur im Kalender.
//
// Bewusst ein explizites Feld und NICHT aus "hat Uhrzeit" abgeleitet: Ein eingeplanter
// Arbeits-Task hat auch eine Uhrzeit und soll trotzdem im Board bleiben.
// Sub-Tasks erben das Kind ihres Eltern-Tasks (die Checkliste eines Termins gehört nicht
// ins Board) – durchgesetzt im TaskRepository, damit es für jeden Anlage-Weg gilt.
public enum TaskKind
{
    Task = 0,
    Appointment = 1,
}
