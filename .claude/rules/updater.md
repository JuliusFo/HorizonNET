---
paths:
  - "Tools/HorizonNET.Updater/**"
  - "Shared/HorizonNET.Shared.Transfer/Update/**"
  - "Server/HorizonNET.Server.Tests/Updater/**"
  - "Server/HorizonNET.Api/Services/Update/**"
  - "scripts/server-install.ps1"
---

# Auto-Update (ADR 0007, `docs/konzept-auto-update.md`)

- **Der Updater läuft als SYSTEM, ausgelöst vom Dienstkonto.** Der Auftrag ist allein der Start der Aufgabe „HorizonNET Update“ (`install`); keine Datei, keine Parameter. „HorizonNET Recover“ (`run`, Systemstart) rollt nur zurück und ist für das Dienstkonto nicht startbar. `--source` gibt es nur auf der Kommandozeile. Download-Adressen baut `UpdatePackage` aus der Version, Links aus der GitHub-Antwort werden nicht benutzt.
- **Ordnerrechte sind Teil der Sicherheit:** Das Dienstkonto darf `app\`, `tools\`, `updater\` und `update\` nur lesen (sonst könnte es tauschen, was SYSTEM ausführt), `backups\` gar nicht sehen. Nur `data\` und `keys\` sind beschreibbar. Neue Ordner in `server-install.ps1` dort einordnen; `backups\` legt nur das Skript an (eigene Rechte).
- **Was das Dienstkonto beschreiben kann, behandelt SYSTEM wie fremde Eingabe:** nie rekursiv kopieren oder löschen, nur bekannte Dateien flach und mit Prüfung auf Reparse Points (Junction, Symlink, Objekt-Manager-Link) – sonst liest, schreibt oder löscht SYSTEM, wohin das Dienstkonto zeigt. Zurückgespielt wird in einen neuen Ordner (`data.restore-<zeit>`), der dann getauscht wird; kein neuer beschreibbarer Ordner, aus dem SYSTEM liest (Review 2026-11-19, S1/S2). Das gilt auch für umbenannte Ordner, die Rechte wandern mit: `*.failed-*` und `*.restore-*` löscht der Updater nie (Review 2026-11-19, zweiter Durchgang).
- `apply` läuft aus `update\staging\X.Y.Z\updater\`: `updater\` wird deshalb **kopiert**, nicht verschoben, und `staging\` räumt erst der nächste Lauf auf.
- Ins Journal **vor** jeder Änderung schreiben (Starttyp, Tausch, Start der neuen Version): Nach einem Absturz entscheidet allein das Journal, was zurückgerollt wird. Die DB kommt nur zurück, wenn die neue Version schon gestartet war – deshalb steht der Dienst während `apply` auf „Deaktiviert“, sonst startet ein Neustart die neue Version unbemerkt (Review 2026-11-19, M1). Auch das Zurückrollen deaktiviert vor dem Stoppen (die Fehleraktion „neu starten“ fährt eine abgestürzte neue Version sonst wieder hoch), setzt den Starttyp am Ende zurück und startet den Dienst immer.
- Paketregeln (Dateiname, „vollständiges Release“) stehen nur in `Shared/…/Update/UpdatePackage.cs`; API und Updater nutzen dieselben. Der Release-Workflow muss dazu passen (`HorizonNET-X.Y.Z-win-x64.zip`, `.sha256` zuletzt).
- Nicht trimmen: Mit gepinntem Runtime-Patch braucht ILLink genau diese Runtime auf dem Build-Rechner. JSON trotzdem über `UpdaterJson` (quellgeneriert).
- **API:** Eine Ausnahme aus `BackgroundService.ExecuteAsync` beendet den ganzen Host – jede Runde absichern. Beim Lesen eines HTTP-Bodys kommt ein Verbindungsabbruch als `IOException` (`HttpIOException`), nicht als `HttpRequestException` (Review 2026-11-19, M2).
- Tests über `UpdaterSandbox` (Temp-Ordner, Attrappen für Dienst, Health, Rechte, Start von apply); laufen in der CI unter Linux, also keine Windows-APIs in Preparer/Installer.
