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
- **Microsoft-Pakete gemeinsam auf denselben Patch heben** – passend zur ASP.NET Core Runtime auf dem vServer (framework-abhängiges Deployment) und zu `ASPNET_VERSION` im `Dockerfile`. Im `CHANGELOG.md` unter **Deployment** vermerken.
- `TreatWarningsAsErrors` ist an: Jede Warnung, auch NuGetAudit (NU1902/NU1903), bricht den Build ab. Beheben statt mit `NoWarn` zudecken; ein unvermeidbares `NoWarn` nur mit Begründung.
- Das `Dockerfile` kopiert vor `dotnet restore` nur einzelne Dateien. Neue Dateien, die der Restore braucht (Props, Konfiguration, neues Projekt), dort ergänzen – die CI baut das Image und merkt es sonst erst dort.
- CI (`.github/workflows/ci.yml`): Build und Tests unter Linux, Docker-Image, Zeilenenden. Job-Namen nicht ohne Grund umbenennen: Sie sind im Ruleset für `master` als Pflicht-Checks eingetragen.
- Zeilenenden: im Repo LF (`* text=auto`). Keine Datei mit CRLF committen.
