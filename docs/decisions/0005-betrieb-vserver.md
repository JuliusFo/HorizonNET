# ADR 0005: Betrieb als Windows-Dienst hinter Cloudflare Tunnel, Docker als Alternative

- Status: akzeptiert
- Datum: 2026-08-27 (Livegang); Docker seit 2026-11-15 (PR #1)

## Kontext

HorizonNET soll von überall erreichbar sein, ohne einen Port am vServer zu öffnen und ohne ein eigenes Zertifikat zu pflegen. Die App selbst kennt genau einen Nutzer.

## Entscheidung

- **Windows-Dienst** auf dem vServer (`UseWindowsService`), lauscht nur auf Loopback (`http://localhost:5000`).
- Davor **Cloudflare Tunnel** (cloudflared) und **Cloudflare Access** mit PIN als erste Hürde; dahinter der Login der App als zweite. TLS terminiert bei Cloudflare; HTTP zwischen cloudflared und Dienst ist Absicht und verlässt die Maschine nie. `ForwardedHeaders` sorgen dafür, dass die App Schema und Client-IP des Besuchers kennt.
- **Framework-abhängig veröffentlicht:** Auf dem Server ist die ASP.NET Core Runtime installiert; sie muss zum Patch der Microsoft-Pakete passen.
- Konfiguration und Secrets als **Umgebungsvariablen** des Dienstes; keine `appsettings.Secrets.json` in Produktion (`docs/deployment-vserver.md`).
- Deployment-Paket per `scripts/deploy-package.ps1` (API-Publish und KeyMigration-Tool), Einrichtung per `scripts/server-install.ps1`.
- **Alternative: Docker-Container** (Linux, `Dockerfile`, `docker-compose.yml`, Daten im Volume `/data`) für Installationen ohne Windows-Server, Anleitung im README.

## Konsequenzen

- Ein Runtime-Sicherheitsupdate heißt zweierlei: Pakete heben **und** die Runtime auf dem vServer (bzw. `ASPNET_VERSION` im Dockerfile).
- Der DPAPI-geschützte Schlüsselring des Windows-Dienstes ist unter Linux nicht lesbar; ein Umzug in den Container geht nur über einen Transport-Ring (`HorizonNET.KeyMigration reprotect`).
- Ohne Cloudflare Access hängt der Schutz allein am Login der App (Rate-Limit, Security-Header).

## Nachtrag (2026-11-19): self-contained statt framework-abhängig

Entscheidung des Nutzers im Rahmen des Auto-Updates (ADR 0007): Das Deployment-Paket bringt die Runtime mit.

- `scripts/deploy-package.ps1` veröffentlicht API und KeyMigration-Tool self-contained für `win-x64` (kein Single-File). Die Runtime im Paket ist der Patch der Microsoft-Pakete (`Microsoft.AspNetCore.*`, `Microsoft.Extensions.*`) aus `Directory.Packages.props`, nicht die des installierten SDK. Dafür reicht das Skript `HorizonRuntimeVersion` an die beiden Projekte durch; die setzen daraus `RuntimeFrameworkVersion`. Eine globale `RuntimeFrameworkVersion` ginge auch an den WASM-Client, und dessen Trimmer bräuchte dann genau diese Runtime lokal. Das Skript bricht ab, wenn die Runtime im Paket nicht diesem Patch entspricht.
- Neben dem ZIP (`HorizonNET-X.Y.Z-win-x64.zip`) liegt eine `.sha256`; im Paket beschreibt `manifest.json` Version, Commit, Build-Zeit und Runtime.
- Visual Studio, `dotnet build` und das `Dockerfile` bleiben framework-abhängig. Die Einstellung steckt nur im Skript.
- Folgen: Auf dem vServer ist keine ASP.NET Core Runtime mehr nötig, sobald ein solches Paket läuft. Ein Runtime-Sicherheitsupdate heißt dann: Microsoft-Pakete heben, Release, Paket einspielen; im Docker-Betrieb zusätzlich `ASPNET_VERSION`. Das Paket ist etwa 120 MB groß statt bisher etwa 60 MB.
