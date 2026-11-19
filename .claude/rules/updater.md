---
paths:
  - "Tools/HorizonNET.Updater/**"
  - "Shared/HorizonNET.Shared.Transfer/Update/**"
  - "Server/HorizonNET.Server.Tests/Updater/**"
  - "scripts/server-install.ps1"
---

# Auto-Update (ADR 0007, `docs/konzept-auto-update.md`)

- **Der Updater läuft als SYSTEM, die Anfrage kommt vom Dienstkonto.** Aus `update\request\request.json` zählt nur `mode`; keine Version, kein Pfad, keine URL. `--source` gibt es nur auf der Kommandozeile (`install`), nie über die Anfrage. Download-Adressen baut `UpdatePackage` aus der Version, Links aus der GitHub-Antwort werden nicht benutzt.
- **Ordnerrechte sind Teil der Sicherheit:** Das Dienstkonto darf `app\`, `tools\`, `updater\` und `update\` nur lesen (sonst könnte es tauschen, was SYSTEM ausführt), `backups\` gar nicht sehen. Nur `data\`, `keys\` und `update\request\` sind beschreibbar. Neue Ordner in `server-install.ps1` dort einordnen.
- `apply` läuft aus `update\staging\X.Y.Z\updater\`: `updater\` wird deshalb **kopiert**, nicht verschoben, und `staging\` räumt erst der nächste Lauf auf.
- Ins Journal **vor** jeder Änderung schreiben (Tausch, Start der neuen Version): Nach einem Absturz entscheidet allein das Journal, was zurückgerollt wird. Die DB kommt nur zurück, wenn die neue Version schon gestartet war.
- Paketregeln (Dateiname, „vollständiges Release“) stehen nur in `Shared/…/Update/UpdatePackage.cs`; API und Updater nutzen dieselben. Der Release-Workflow muss dazu passen (`HorizonNET-X.Y.Z-win-x64.zip`, `.sha256` zuletzt).
- Nicht trimmen: Mit gepinntem Runtime-Patch braucht ILLink genau diese Runtime auf dem Build-Rechner. JSON trotzdem über `UpdaterJson` (quellgeneriert).
- Tests über `UpdaterSandbox` (Temp-Ordner, Attrappen für Dienst, Health, Rechte, Start von apply); laufen in der CI unter Linux, also keine Windows-APIs in Preparer/Installer.
