---
paths:
  - "Server/HorizonNET.Api/Program.cs"
  - "Server/HorizonNET.Api/Controllers/**"
  - "Server/HorizonNET.Api/appsettings*.json"
---

# API, Auth und Hosting

Hosting-Entscheidung: ADR 0001.

- **Auth:** Die Fallback-Policy verlangt für jeden Endpunkt eine Anmeldung. `[AllowAnonymous]` nur an einzelnen Actions, nie am Controller – dort ließe es sich per `[Authorize]` nicht mehr zurücknehmen (siehe `AuthController`). Neue anonyme Endpunkte nur mit Begründung im Kommentar.
- **Same-Origin:** Die API liefert den WASM-Client aus (`MapFallbackToFile`). Kein CORS, Cookies ohne Sonderbehandlung. `UseBlazorFrameworkFiles` nicht wieder einführen (Kommentar in `Program.cs`).
- **Security-Header und CSP** gelten für jede Antwort, auch statische Dateien. Die CSP erlaubt keine fremden Hosts für Skripte, `connect-src` und Bilder – ein Feature, das das braucht, ist eine bewusste Entscheidung des Nutzers.
- **Hinter dem Proxy:** `X-Forwarded-*` gilt nur von `ForwardedHeaders:KnownNetworks`. Das Login-Rate-Limit hängt an der IP, also an genau diesen Headern.
- **Secrets:** `appsettings.Secrets.json` nur in Development, nie im Publish oder Image (`CopyToPublishDirectory="Never"`, `.dockerignore`). In Produktion kommen Secrets als Umgebungsvariablen (`docs/deployment-vserver.md`, `.env.example`). Neue Einstellung → beide Stellen und den Abschnitt **Deployment** im `CHANGELOG.md` pflegen.
- Zugangsdaten (Tokens, App-Passwort, PAT) nie in einer Antwort an den Client.
- Eingaben validieren und mit 400 antworten statt mit Standardwerten weiterzurechnen oder mit 500 abzubrechen (`[BindRequired]`, Bereichsprüfung bei Datumswerten; Review PR #1, Wochenabschluss).
