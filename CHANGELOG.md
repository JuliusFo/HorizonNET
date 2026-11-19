# Changelog

Alle nennenswerten Änderungen an HorizonNET stehen hier.
Das Format folgt [Keep a Changelog](https://keepachangelog.com/de/1.1.0/), die Versionen folgen seit dem Livegang [SemVer](https://semver.org/lang/de/).

Abschnitte je Version in dieser Reihenfolge, leere weglassen: **Sicherheit**, **Neu**, **Geändert**, **Behoben**, **Intern** (Build, CI, Doku – nichts, was man in der App sieht). Ein Abschnitt **Deployment** am Ende nennt, was auf dem vServer zu tun ist (Migrationen, Runtime, Einstellungen).

## [Unreleased]

### Behoben
- Schlüsselring-Umzug (`HorizonNET.KeyMigration`): Der Azure-DevOps-PAT wird jetzt mit umgeschlüsselt. Bisher fehlte er in der Spaltenliste und wäre nach einem Umzug (etwa in einen Docker-Container) unlesbar gewesen; die Verbindung hätte neu eingerichtet werden müssen. Ein Test vergleicht die Liste jetzt mit dem Datenmodell.

### Intern
- Gemeinsame Compiler-Einstellungen zentral in `Directory.Build.props`, inklusive `TreatWarningsAsErrors`: Jede neue Warnung bricht den Build ab, auch NuGetAudit-Meldungen.
- SDK per `global.json` gepinnt, repo-lokales `nuget.config` (nur nuget.org, Source Mapping).
- Central Package Management: alle Paketversionen in `Directory.Packages.props`.
- CI mit GitHub Actions: Build und Tests, Docker-Image und Zeilenenden bei jedem Push, jedem PR und montags.
- Dependabot: wöchentliche Updates der GitHub Actions, Sicherheitsupdates für NuGet (Microsoft-Pakete gebündelt).
- `CLAUDE.md` mit Projektanweisungen, dieser Changelog und der Skill `/release`.
- Skill `/branch-code-review` mit HorizonNET-Checkliste, Regeln je Bereich in `.claude/rules/` und Architekturentscheidungen (ADRs 0001–0006) in `docs/decisions/`.
- Deployment-Paket self-contained (`win-x64`): `scripts/deploy-package.ps1` bringt die Runtime im Patch der Microsoft-Pakete mit, legt `manifest.json` ins Paket und eine `.sha256` daneben und bricht ab, wenn Entwicklungs-Konfiguration im Paket landen würde oder die Runtime nicht passt. Das ZIP heißt jetzt `HorizonNET-X.Y.Z-win-x64.zip`. Vorbereitung für das Auto-Update (ADR 0007, vorgeschlagen).
- Release-Workflow (`.github/workflows/release.yml`): Baut beim Veröffentlichen eines GitHub-Releases das Deployment-Paket aus dem Tag und hängt ZIP und `.sha256` an das Release; `/release` wartet darauf.
- Updater `Tools/HorizonNET.Updater` (im Paket unter `updater\`): lädt das Paket des neuesten Releases, prüft die Prüfsumme, stoppt den Dienst, sichert DB und Schlüsselring (die letzten 5 Sicherungen bleiben), tauscht `app\`, `tools\` und `updater\`, startet den Dienst und prüft `/api/version`. Bei einem Fehler rollt er Programm und, falls die neue Version schon lief, DB zurück; ein Journal macht abgebrochene Läufe wiederherstellbar. Das Paket wächst dadurch auf etwa 160 MB.

### Deployment
- Das Paket ist self-contained: Beim Einspielen den Inhalt von `app\` und `tools\` ersetzen statt darüberzukopieren. Danach braucht der vServer keine ASP.NET Core Runtime mehr; sie kann bleiben, bis das neue Paket einmal sauber gelaufen ist.

## [1.3.0] - 2026-11-15

Erstes getaggtes Release. Enthält die Features aus PR #1 von mahe4 und ein Sicherheitsupdate der .NET-Pakete.

### Sicherheit
- Microsoft-Pakete von 10.0.10 auf 10.0.15, unter anderem wegen GHSA-g27p-qf6j-c96c und GHSA-qjpx-jqm5-h2c4 in `Microsoft.AspNetCore.Components.WebAssembly.Server` sowie Lücken in `System.Security.Cryptography.Pkcs`/`Xml` (KeyMigration-Tool).
- Die manuelle Übersteuerung auf `Microsoft.OpenApi` 2.7.5 entfällt, `Microsoft.AspNetCore.OpenApi` 10.0.15 bringt selbst 2.13.0 mit.

### Neu
- Docker-Deployment: Dockerfile und `docker-compose.yml` als Alternative zum Windows-Dienst, Health-Endpoint `/healthz` mit `HEALTHCHECK`.
- Import aus Azure DevOps: Work Items eines Azure-DevOps-Projekts als Tasks übernehmen. Der PAT wird per DataProtection verschlüsselt gespeichert.
- Kalendersync wahlweise mit Nextcloud (CalDAV) statt Google. Beim Neuverbinden desselben Anbieters bleiben die Event-IDs erhalten.
- Wochenabschluss: Seite `/wochenabschluss` mit dem, was in einer Woche erledigt wurde und was noch offen ist, wahlweise je Arbeitsbereich, als Mail per `mailto:`.

### Behoben
- Entwicklungs-Secrets (`appsettings.Secrets.json`) und `appsettings.Development.json` landen nicht mehr im Publish-Ordner.
- Keine Null-Dereferenzierung mehr beim Übertragen der Slot-Zeiten einer Terminserie.

### Deployment
- Vorher die Datenbank sichern: Drei Migrationen laufen beim Start (`AddAzureDevOps`, `AddCalDavConnection`, `AzureDevOpsWorkItemPerOrganization`).
- ASP.NET Core Runtime auf dem vServer auf 10.0.15 heben.

## [1.2.0] - 2026-09-25

### Neu
- Termine vs. Aufgaben: `TaskKind` (Aufgabe/Termin), Terminserien mit Slots, ein Generator als Hosted Service und die Serien-Verwaltung im Kalender.

### Deployment
- Zwei Migrationen (`AddTaskKind`, `AddTaskSeries`), werden beim Start automatisch angewendet.

## [1.1.0] - 2026-09-22

### Neu
- „Titel kopieren“ auf der Task-Detailseite.
- Heute-Seite: Fälligkeit leeren per Button.

### Geändert
- Mobile Ansichten überarbeitet.

### Behoben
- Heute-Seite: Beim Abhaken eines Tasks wurde der nächste in der Liste optisch mit abgehakt.

## [1.0.3] - 2026-09-22

### Behoben
- Journal und Notizen verlieren getippten Text nicht mehr, wenn das Auto-Save neu rendert.

## [1.0.2] - 2026-09-22

### Behoben
- Kanban-Karte ohne sporadisches Projektname-Label (die Farbe zeigt das Projekt).

## [1.0.1] - 2026-08-27

Erster Stand nach dem Livegang (horizon.cloudfox.xyz). Ab hier gilt SemVer, die Kopplung „Minor = Phasennummer“ endet. Eine Version 1.0.0 gab es nicht.

### Neu
- Login: Die API verlangt eine Anmeldung, Login im Client, „Passwort ändern“ in den Einstellungen.
- Same-Origin-Hosting: Die API liefert den Blazor-Client mit aus.
- Betrieb als Windows-Dienst hinter Cloudflare Tunnel, Skripte für Deployment und Server-Installation.
- `HorizonNET.KeyMigration` für den Umzug des DataProtection-Schlüsselrings.
- Beim Abschließen oder Abbrechen eines Tasks werden auch seine Sub-Tasks gesetzt (mit Rückfrage).

### Geändert
- Projektkacheln zählen nur Haupt-Tasks.

### Sicherheit
- Rate-Limit für den Login, Forwarded Headers nur von bekannten Proxys, Security-Header (CSP, `X-Frame-Options`, `nosniff`, `Referrer-Policy`).
- `appsettings.Secrets.json` wird nur noch in der Development-Umgebung gelesen.

## Vor dem Livegang

Bis zum Livegang zählte die Minor-Version die abgeschlossenen Phasen. Details stehen in der App-Datenbank (Projekte „Phase X – …“).

- **0.16.0** (2026-08-25): Phasen 12–16, unter anderem kleinere UI/UX-Verbesserungen, Notizen in Ordnern und Kommentare, Journal mit Feldverschlüsselung, Refactoring und Erinnerungen für Tasks. Die Nummern 0.12.0–0.15.0 wurden übersprungen.
- **0.11.0** (2026-07-17): Phase 11 – Sport-Tracking.
- **0.10.0** (2026-07-17): Phase 10 – UI/UX für Arbeitsaufgaben.
- **0.9.0** (2026-07-15): Versionierung für API und Client eingeführt.

[Unreleased]: https://github.com/JuliusFo/HorizonNET/compare/v1.3.0...HEAD
[1.3.0]: https://github.com/JuliusFo/HorizonNET/releases/tag/v1.3.0
