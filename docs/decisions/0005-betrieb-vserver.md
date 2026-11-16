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
