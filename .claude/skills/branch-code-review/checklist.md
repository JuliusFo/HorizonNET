# Review-Checkliste (HorizonNET)

Gehört zu `/branch-code-review`. Jeder Reviewer-Agent bekommt diese Datei; sie nennt, was für dieses Projekt gilt. Allgemeines Review-Handwerk (Logikfehler, Randfälle, Ressourcen) gilt zusätzlich.

## Schwere

- **S**: Datenverlust, falsche oder doppelte Daten (auch im Kalender), Sicherheitslücke, App startet nicht mehr (Migration, Konfiguration)
- **M**: falsches Verhalten in realistischen Fällen oder eine klare Wartungslast
- **K**: Kleinigkeit

## Korrektheit

- **Soft-Delete:** Entitäten mit `DeletedAt` haben einen globalen Query-Filter. `IgnoreQueryFilters()` nur bewusst (Papierkorb, Wiederherstellen, Duplikat-Prüfung) – und dann `DeletedAt` selbst prüfen. Kinder eines gelöschten Elternteils (Task → Sub-Tasks, Projekt → Tasks, Arbeitsbereich → Projekte): Was zeigt die App, was liefern Berichte und Suche?
- **Zeit:** `CompletedAt`, Fälligkeiten und Wochengrenzen sind lokale Zeit (Server: Europe/Berlin, CI: UTC). Tagesgrenzen, Sommerzeit, ISO-Kalenderwoche über den Jahreswechsel. Kein `DateTime.Now` in Logik, die Tests prüfen sollen.
- **Auto-Save im Client** (Notizen, Journal, Zeichnungen): Debounce, `ShouldRender` beim Tippen unterdrückt, `FlushAsync` vor Wechsel oder Verlassen. Geht getippter Text verloren, wenn eine Antwort spät kommt, ein Render dazwischenfunkt oder die Komponente schon entsorgt ist (1.0.3)?
- **Veraltete Antworten:** Schnelles Blättern oder Umschalten im Client – kann eine ältere Antwort eine neuere überschreiben (Wochenabschluss, Projekt-Dialog in PR #1)?
- **Parallelität im Server:** Hosted Services (`TaskSeriesGenerator`, Kalender-Abgleich) gegen normale Requests. Prüfen-dann-Anlegen ohne eindeutigen Index erzeugt Dubletten.
- **Kalender-Sync:** alles über `CalendarSyncCoordinator`; Event-IDs nur beim echten Anbieterwechsel verwerfen (`calendar.eventIdProvider`); Neuverbinden desselben Anbieters darf keine Termine verdoppeln. Details: `.claude/rules/calendar.md`.
- **Migrationen:** laufen beim Start auch auf dem vServer. `Down` gespiegelt, Daten-SQL gegen den echten Spaltentyp (`TaskItem.Status` ist eine Zahl, `Priority` ein String), verlustfrei bei Umbenennungen. Details: `.claude/rules/migrations.md`.

## Sicherheit: Eingaben von außen bis dahin verfolgen, wo sie landen

Ein fester Durchgang, kein Gesamteindruck: Für jede Quelle, die die Änderung berührt, den Wert bis zu jeder Senke verfolgen und die Senken nennen, auch wenn nichts falsch ist.

Quellen (nicht vertrauenswürdig):
- Request-Bodies, Query-Strings, Route-Werte der API.
- Antworten fremder Server: Google, CalDAV/Nextcloud (Redirects, `href`s, ICS-Inhalte), Azure DevOps (JSON, HTML in Beschreibungen).
- Vom Nutzer eingefügter Text: Notizen (HTML aus dem Editor), Links, `mailto:`-Empfänger.
- Header hinter dem Proxy: `X-Forwarded-*` gelten nur von bekannten Netzen (`ForwardedHeaders:KnownNetworks`).

Senken (nie ungeprüft):
- **Zugangsdaten:** Refresh-Token, App-Passwort, PAT nur verschlüsselt speichern (`EncryptedConverter`), nie an den Client schicken, nie loggen, nur an den eigenen Server (Schema, Host und Port festgenagelt, kein Downgrade auf http – `EnsureSameOrigin`).
- **HTML im Client:** `MarkupString` und `innerHTML` nur mit bereinigtem oder eigenem Inhalt. Die CSP erlaubt keine fremden Quellen und soll es nicht.
- **Auth:** Fallback-Policy verlangt Anmeldung; `[AllowAnonymous]` nur an einzelnen Actions, nie am Controller. Rate-Limit am Login bleibt.
- **URLs und Links:** `mailto:`, externe Links, Redirect-Ziele kodiert und auf erlaubte Schemata geprüft.
- **Größe und Last:** Antworten fremder Server mit Größen- und Zeitlimit lesen, Paging mit Continuation statt fester Obergrenze.
- **Logs und Fehlermeldungen:** keine Secrets, keine Journal-Inhalte.

Ein Sicherheitsbefund nennt den konkreten Weg von der Quelle zur Senke, die fehlende Prüfung an der Grenze und einen Test, der ohne den Fix scheitert.

## Daten und Verschlüsselung

- Neue verschlüsselte Spalte: Protector-Purpose eindeutig und nie mehr ändern; zusätzlich in `Tools/HorizonNET.KeyMigration/EncryptedColumns.cs` eintragen, sonst geht sie beim Schlüsselring-Umzug verloren (`KeyMigrationColumnsTests` prüft das).
- Kein SQLCipher (`docs/konzept-journal.md`).
- Suche über verschlüsselte Felder: SQL-Vorfilter, Rest im Speicher entschlüsseln.

## UI

- Texte auf Deutsch; wo eine Komponente schon `IStringLocalizer` nutzt, dort weiter über die resx.
- Löschen mit Bestätigung (`ConfirmService`), Fehler beim Netzwerkzugriff sichtbar (`ApiErrorHandler`, `ToastService`).
- Mobile Ansicht mitgedacht (Heute-Seite, Kanban).
- SVG-Koordinaten mit `CultureInfo.InvariantCulture`; `<text>` ist in Razor ein Steuer-Tag.

## Tests

- Server: `TestDatabase` (In-Memory-SQLite, frischer Kontext je Schritt), Controller direkt instanziiert. Details: `.claude/rules/tests.md`.
- Ein Bugfix kommt mit einem Test, der ohne ihn scheitert, wo machbar.
- Die CI testet unter Linux (UTC, Groß-/Kleinschreibung in Pfaden, `/` als Trenner).

## Doku und Betrieb

- `CHANGELOG.md` unter `[Unreleased]` eingetragen, bei Migrationen oder neuen Einstellungen auch im Abschnitt **Deployment**.
- `README.md` (Docker), `docs/deployment-vserver.md`, `.env.example` und `CLAUDE.md` nach der Änderung noch richtig.
- Neue Dateien, die der Restore braucht, im `Dockerfile` vor `dotnet restore`; Microsoft-Pakete gemeinsam auf einem Patch, passend zu `ASPNET_VERSION` (das self-contained Deployment-Paket nimmt diesen Patch als Runtime).
- PowerShell-Skripte als UTF-8 mit BOM.

## Kein Befund

- Entscheidungen des Nutzers: ADRs in `docs/decisions/`, Kommentare im Code mit Begründung, Konzepte in `docs/`.
- Bewusst Zurückgestelltes (etwa Journal 14m–14o), solange die Änderung es nicht verschlimmert.
- Stilfragen ohne konkrete Kosten.
