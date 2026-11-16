---
paths:
  - "Server/HorizonNET.Api/Services/Calendar/**"
  - "Server/HorizonNET.Api/Services/GoogleCalendarService.cs"
  - "Server/HorizonNET.Api/Controllers/CalendarController.cs"
  - "Server/HorizonNET.Api/Controllers/GoogleController.cs"
  - "Server/HorizonNET.Api/Controllers/CalDavController.cs"
---

# Kalender-Sync (Google oder Nextcloud/CalDAV)

Entscheidung und Hintergrund: ADR 0004.

- Genau ein Anbieter ist verbunden, nie beide. Der Sync geht nur in eine Richtung (App → Kalender); Änderungen im Kalender überschreibt die App beim nächsten Speichern des Tasks.
- **Event-IDs nur beim echten Anbieterwechsel verwerfen:** `CalendarSyncService` vergleicht den bisherigen Anbieter (`calendar.eventIdProvider`, Bestand ohne Eintrag = Google) mit dem neuen. Neuverbinden desselben Anbieters (abgelaufenes Token, Trennen und Verbinden) setzt mit den alten IDs fort; Trennen löscht keine IDs. Sonst entstehen doppelte Termine (Blocker im Review von PR #1).
- **Alles über `CalendarSyncCoordinator`:** Einzel-Sync aus Requests und Komplett-Abgleich im Hintergrund laufen unter derselben Sperre; die Event-ID wird unter der Sperre frisch gelesen. Kein zweiter Weg an der Sperre vorbei, kein verschachteltes Sperren.
- **CalDAV-Zugangsdaten nur an den eigenen Server:** Vor jeder Anfrage, auch der ersten und nach jedem Redirect, `EnsureSameOrigin` (Schema, Host und Port gegen die gespeicherte Server-URL, kein Wechsel von https auf http). Vom Server gelieferte `href`s genauso prüfen.
- Eigene Termine haben die UID-Vorsilbe `horizonnet-task-` (`CalDavCalendarService.OwnUidPrefix`). Beim Lesen fremder Termine immer ausblenden, auch ohne bekannte ID – sonst erscheinen eigene Termine doppelt.
- Ein 401 auf PUT/DELETE ist nicht automatisch ein Grund, die Verbindung zu löschen (Brute-Force-Schutz von Nextcloud).
- Refresh-Token und App-Passwort liegen verschlüsselt in der DB und gehen nie an den Client.
