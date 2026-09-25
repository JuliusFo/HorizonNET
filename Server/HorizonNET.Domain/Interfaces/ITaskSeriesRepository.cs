using HorizonNET.Domain.Entities;

namespace HorizonNET.Domain.Interfaces;

// Was eine Änderung an einer Serie mit ihren materialisierten Terminen gemacht hat. Der
// Aufrufer (Controller/Generator) braucht das für den Google-Sync, der nicht im
// Repository liegt: Upserted → spiegeln, Removed → Google-Event entfernen.
public sealed record TaskSeriesChange(
    TaskSeries Series,
    IReadOnlyList<TaskItem> Upserted,
    IReadOnlyList<TaskItem> Removed);

public interface ITaskSeriesRepository
{
    // Alle aktiven (nicht gelöschten) Serien inkl. Slots und Projekt, nach Titel.
    Task<IEnumerable<TaskSeries>> GetAllAsync();

    Task<TaskSeries?> GetByIdAsync(int id);

    // Legt Serie samt Slots an (Slots kommen mit im Objekt). Materialisiert wird hier
    // nichts – dafür MaterializeAsync.
    Task<TaskSeries> CreateAsync(TaskSeries series);

    // Vollersatz der Kopf-Felder; Slots werden abgeglichen: gleiche Id → aktualisiert,
    // ohne/unbekannte Id → neu, fehlende → entfernt. Ids bleiben damit stabil.
    //
    // Wirkung auf ZUKÜNFTIGE materialisierte Termine (SeriesDate >= heute):
    //  - entfernter Slot → seine Termine werden soft-gelöscht (Removed);
    //  - Kopf-Felder (Titel, Beschreibung, Projekt, Kind, Priorität, Erinnerung) werden
    //    auf offene Termine übernommen; Uhrzeiten nur, wenn der Termin nicht verschoben
    //    wurde (DueDate == SeriesDate) – ein bewusst verlegter Einzeltermin bleibt (Upserted).
    // Vergangene Termine sind Historie und bleiben unangetastet. null = nicht gefunden.
    Task<TaskSeriesChange?> UpdateAsync(int id, TaskSeries series);

    // Materialisiert alle aktiven Serien für [from, to]: je Slot und passendem Kalendertag
    // ein Termin, sofern für (Serie, Slot, Datum) noch KEINE Zeile existiert – auch keine
    // soft-gelöschte (sonst kehrte ein abgesagter Termin zurück). Liefert die neuen Termine.
    Task<IReadOnlyList<TaskItem>> MaterializeAsync(DateOnly from, DateOnly to);

    // Soft-Delete der Serie: zukünftige Termine (SeriesDate >= heute) gehen mit demselben
    // Zeitstempel mit (Removed), vergangene bleiben als Historie. null = nicht gefunden.
    Task<TaskSeriesChange?> DeleteAsync(int id);

    // Holt Serie und die im selben Vorgang gelöschten Termine zurück (Upserted).
    Task<TaskSeriesChange?> RestoreAsync(int id);

    // Soft-gelöschte Serien (für den Papierkorb), zuletzt gelöscht zuerst.
    Task<IEnumerable<TaskSeries>> GetDeletedAsync();

    // Endgültiges Löschen einer soft-gelöschten Serie inkl. Slots und der mit ihr
    // gelöschten Termine (nicht umkehrbar). Vergangene Termine verlieren nur den Bezug.
    Task<bool> PurgeAsync(int id);
}
