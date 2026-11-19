---
paths:
  - "Directory.Build.props"
  - "Directory.Packages.props"
  - "global.json"
  - "nuget.config"
  - "Dockerfile"
  - "docker-compose.yml"
  - ".github/**"
  - "**/*.csproj"
---

# Build, Pakete, CI

- Paketversionen nur in `Directory.Packages.props` (Central Package Management, transitives Pinning an). In den `.csproj` keine `Version=`; Abweichungen per `VersionOverride` mit Begründung.
- **Microsoft-Pakete gemeinsam auf denselben Patch heben** – passend zu `ASPNET_VERSION` im `Dockerfile`. Das self-contained Deployment-Paket nimmt diesen Patch als Runtime (`deploy-package.ps1` liest ihn aus `Directory.Packages.props` und prüft das Ergebnis). Im `CHANGELOG.md` unter **Deployment** vermerken.
- `RuntimeFrameworkVersion` nie global (`-p:` an einem Projekt mit Referenz auf den WASM-Client) setzen: Sie gilt dann auch für den Client, und dessen Trimmer braucht genau diese Runtime lokal. Deshalb der Umweg über `HorizonRuntimeVersion` in API und KeyMigration-Tool.
- `TreatWarningsAsErrors` ist an: Jede Warnung, auch NuGetAudit (NU1902/NU1903), bricht den Build ab. Beheben statt mit `NoWarn` zudecken; ein unvermeidbares `NoWarn` nur mit Begründung.
- Das `Dockerfile` kopiert vor `dotnet restore` nur einzelne Dateien. Neue Dateien, die der Restore braucht (Props, Konfiguration, neues Projekt), dort ergänzen – die CI baut das Image und merkt es sonst erst dort.
- CI (`.github/workflows/ci.yml`): Build und Tests unter Linux, Docker-Image, Zeilenenden. Job-Namen nicht ohne Grund umbenennen: Sie sind im Ruleset für `master` als Pflicht-Checks eingetragen.
- Release (`.github/workflows/release.yml`): baut bei `release: published` das Paket mit `deploy-package.ps1` und hängt es an. Nur der Upload-Job darf schreiben und führt keinen Repo-Code aus; Actions dort auf Commit-SHAs gepinnt. Den Dateinamen `HorizonNET-X.Y.Z-win-x64.zip` (+ `.sha256`) erwarten Updater und API – nicht ändern, ohne beide nachzuziehen.
- Zeilenenden: im Repo LF (`* text=auto`). Keine Datei mit CRLF committen.
