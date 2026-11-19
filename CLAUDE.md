# HorizonNET – persönlicher Tages-Organizer

> Projektanweisungen für Claude Code. Arbeitssprache mit dem Nutzer: **Deutsch**. Code-Kommentare und Doku: **Deutsch**. Commit-Messages: **Englisch**.
> Release-Stand: `<Version>` in `Directory.Build.props`, die Tags `vX.Y.Z` und `CHANGELOG.md`.

Diese Datei lädt in jede Session und enthält nur, was immer gilt. Die Fallen eines Bereichs stehen in `.claude/rules/` und laden, sobald eine passende Datei gelesen oder bearbeitet wird (`paths` im Kopf jeder Regel). Neue Fallen dort eintragen, wo sie gebraucht werden; eine neue Regel nur mit gültigem `paths`-Kopf (ohne lädt sie immer). `docs/` ist bis auf `docs/decisions/` per `.gitignore` **nicht versioniert** und existiert nur auf dem Rechner des Nutzers.

| Datei | Lesen … |
|---|---|
| `docs/decisions/` – ADRs (Hosting, Datenhaltung, Verschlüsselung, Kalender, Betrieb, Versionierung) | vor Stack- oder Architekturänderungen; eine neue Entscheidung bekommt eine ADR |
| `.claude/skills/` – `/release`, `/branch-code-review` (mit `checklist.md`) | beim Release bzw. Code-Review |
| `docs/deployment-vserver.md` | vor jedem Deployment und vor Änderungen an `scripts/` |
| `docs/konzept-*.md` (Journal, DPAPI-Umzug, Versionierung, Sport, Zeichnungen, Detail/Notizen/Daily, Auto-Update) | bevor der jeweilige Bereich angefasst wird |
| `README.md` | Docker-Betrieb, Kalender-Sync, Backup |

## 1. Ziel und Betrieb

Task-Planer, Kalender, Notizen und Journal für einen einzelnen Nutzer. **Live seit 27.08.2026** unter https://horizon.cloudfox.xyz: Cloudflare Access (PIN) → Cloudflare Tunnel → Windows-Dienst auf dem vServer. Alternativ läuft die App als Docker-Container (README).

Der Nutzer ist erfahrener .NET-Entwickler (Visual Studio, Blazor). Erklärungen auf Senior-Niveau, keine Grundlagen.

## 2. Stack

| Bereich | Entscheidung | Hinweise |
|---|---|---|
| Runtime | **.NET 10**, SDK per `global.json` (`rollForward: latestFeature`) | Deployment-Paket (`scripts/deploy-package.ps1`) **self-contained** `win-x64`, Runtime = Patch der Microsoft-Pakete (ADR 0005, Nachtrag). Docker und lokale Builds bleiben framework-abhängig. |
| Solution | **`HorizonNET.slnx`** (eine Root-Solution), Central Package Management (`Directory.Packages.props`) | Gemeinsame Compiler-Einstellungen in `Directory.Build.props`, inkl. `TreatWarningsAsErrors`. Repo-lokales `nuget.config` (nur nuget.org, Source Mapping). |
| Hosting | ASP.NET-API liefert den **Blazor-WASM-Client** mit aus (Same-Origin) | Kein CORS, Cookie-Auth ohne Sonderbehandlung. Betrieb als Windows-Dienst (`UseWindowsService`) oder Container. |
| UI | **Radzen.Blazor**, Bootstrap | Texte über `Microsoft.Extensions.Localization`. |
| Daten | **EF Core + SQLite** im WAL-Modus (`horizonnet.db`) | Migrationen laufen beim Start automatisch (`db.Database.Migrate()`). |
| Verschlüsselung | DataProtection für sensible Spalten (`EncryptedConverter`): Journal-Text, Zugangsdaten (Google, CalDAV, Azure-DevOps-PAT) | Kein SQLCipher (verworfen, `docs/konzept-journal.md`). **Schlüsselring und DB gehören zusammen ins Backup** – ohne Ring sind die Inhalte verloren. Umzug: `Tools/HorizonNET.KeyMigration`. |
| Kalender | `ICalendarProvider`: **Google** oder **Nextcloud/CalDAV**, nie beide | Einweg-Sync. Event-IDs nur beim echten Anbieterwechsel verwerfen (`calendar.eventIdProvider`). Sync und Abgleich laufen über `CalendarSyncCoordinator` (eine Sperre). |
| Tests | **xUnit v2** (VSTest) | `HorizonNET.Server.Tests`, `HorizonNET.Client.Tests`. |

## 3. Solution-Struktur

```
Shared/HorizonNET.Shared.Transfer/   DTOs und Enums zwischen Client und Server
Server/HorizonNET.Domain/            Entities, Interfaces
Server/HorizonNET.Data/              AppDbContext, Migrationen, Repositories
Server/HorizonNET.Api/               Controller, Services, Program.cs; liefert den Client aus
Client/HorizonNET.App/               Blazor WASM (Pages, Components, Services)
Tools/HorizonNET.KeyMigration/       Schlüsselring-Umzug (DPAPI ↔ Transport-Ring), auch im Deployment-Paket
scripts/                             Migrationen, Deployment-Paket, Server-Installation, Backup
```

## 4. Arbeitsweise

- **Entschieden ist nur, was der Nutzer entschieden hat.** Regeln und Festlegungen, die Claude selbst vorschlägt, stehen in Doku und Kommentaren als „Vorschlag (nicht bestätigt)“, bis der Nutzer zustimmt.
- **Task für Task:** Größere Arbeit in einzelne Tasks zerlegen, jeden umsetzen → bauen → testen → dem Nutzer melden, dass er committfähig ist, **dann anhalten**. Kein Sammel-Diff über mehrere Tasks.
- **Der Nutzer committet in der Regel selbst.** Nur auf ausdrückliche Bitte committen, pushen oder taggen. Nie ungeprüft behaupten, etwas sei „noch nicht committet“ – vorher `git status`/`git log` ansehen.
- **Phasen** sind Projekte in der App-DB (`Phase X - <Titel>` in `Projects`/`Tasks`, nur `DeletedAt IS NULL`), nicht Dateien im Repo. Teilschritte innerhalb einer Phase heißen 9a, 9b, …
- **Fertig heißt:** `dotnet build` (warnungsfrei, Warnungen sind Fehler) und `dotnet test` im Root grün; bei sichtbaren Änderungen in der laufenden App geprüft; Eintrag unter `[Unreleased]` in `CHANGELOG.md`.
- **Commits:** Präfix wie bisher (`Feat:`, `Fix:`, `Chore:`, `Docs:`), Englisch, kurz und thematisch.
- **Versionierung:** SemVer seit dem Livegang. Sichtbare Änderungen (und interne, soweit erwähnenswert) gleich beim Umsetzen unter `[Unreleased]` in `CHANGELOG.md` eintragen – Abschnitte Sicherheit, Neu, Geändert, Behoben, Intern, Deployment. Ein Release macht der Skill `/release` (Version, CHANGELOG-Abschnitt, Tag, GitHub-Release, Hinweise fürs Deployment); `<Version>` sonst nicht von Hand anheben.

## 5. Fallen

- **Laufender Client:** Der Nutzer hat API und Client oft aus Visual Studio laufen. Ein `dotnet build` macht den laufenden Client kaputt (Runtime-Hash passt nicht mehr) – danach muss er neu gestartet werden. Vorher Bescheid geben.
- **Migrationen:** über `scripts/add-migration.ps1 <Name>` (setzt Projekt- und Startprojekt). Sie laufen beim nächsten Start auch auf dem vServer – vor dem Deployment die DB sichern. `Down` sauber spiegeln.
- **PowerShell-Skripte (`.ps1`) als UTF-8 mit BOM speichern.** Windows PowerShell 5.1 auf dem vServer liest sie sonst als ANSI, und ein „–“ zerreißt Strings. Nach dem Schreiben umkodieren und mit `[System.Management.Automation.Language.Parser]::ParseFile` auf 0 Fehler prüfen.
- **Secrets:** `appsettings.Secrets.json` und `appsettings.Development.json` gelangen nie ins Publish (`CopyToPublishDirectory="Never"`) und nie ins Image. Produktion bekommt Secrets über Umgebungsvariablen.
- **Paket-Updates:** Versionen nur in `Directory.Packages.props`. Die Microsoft-Pakete gemeinsam auf denselben Patch heben und im `Dockerfile` `ASPNET_VERSION` nachziehen; der vServer bekommt die Runtime mit dem nächsten Paket. Neue Dateien, die der Restore braucht, auch im `Dockerfile` vor `dotnet restore` kopieren.
- **Login:** Die App liegt hinter einem Login, Passwörter tippt Claude nicht. Reine Layout-Checks über einen statischen Mock in `Client/HorizonNET.App/wwwroot/` (danach bauen und den Dev-Server neu starten, damit er im Static-Web-Assets-Manifest steht; Datei anschließend wieder löschen). Für Logik hinter dem Login den Nutzer bitten, sich im Browser-Pane anzumelden.
