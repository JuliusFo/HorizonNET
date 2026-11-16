# ADR 0004: Kalender-Sync – Google oder CalDAV, nur in eine Richtung

- Status: akzeptiert
- Datum: 2026-11-15 (PR #1, Release 1.3.0); Google-Sync schon vorher

## Kontext

Geplante Tasks erscheinen als Termine in einem externen Kalender. Lange gab es nur Google. PR #1 brachte Nextcloud per CalDAV als Alternative für Installationen ohne Google-Konto. Das Review fand dabei drei Fehlerklassen: verdoppelte Termine beim Neuverbinden, Rennen zwischen Hintergrund-Abgleich und Requests, und Zugangsdaten, die per Redirect oder `href` an einen fremden Server gehen konnten.

## Entscheidung

- Ein `ICalendarProvider` je Anbieter (`GoogleCalendarService`, `CalDavCalendarService`), davor die Fassade `CalendarSyncService`. **Genau ein Anbieter** ist verbunden, nie beide.
- **Einweg-Sync** App → Kalender. Änderungen im Kalender überschreibt die App beim nächsten Speichern des Tasks; fremde Termine werden nur gelesen und angezeigt.
- Event-IDs bleiben beim Neuverbinden desselben Anbieters erhalten und werden nur beim echten Anbieterwechsel verworfen (Setting `calendar.eventIdProvider`). Trennen löscht keine IDs.
- Einzel-Sync und Komplett-Abgleich laufen über den Singleton `CalendarSyncCoordinator` (eine Sperre); die Event-ID wird unter der Sperre gelesen.
- CalDAV: Zugangsdaten nur an den gespeicherten Server (`EnsureSameOrigin` vor jeder Anfrage und nach Redirects, kein Downgrade auf http). Eigene Termine tragen die UID-Vorsilbe `horizonnet-task-` und werden beim Lesen immer ausgeblendet.

## Konsequenzen

- Beim Wechsel des Anbieters bleiben die alten Termine im alten Kalender stehen (dokumentiert im README).
- Kein Konfliktabgleich, kein `If-Match`/ETag – für einen Einweg-Sync bewusst weggelassen.
- Ein neuer Anbieter implementiert `ICalendarProvider` und hält dieselben Regeln ein (`.claude/rules/calendar.md`).
