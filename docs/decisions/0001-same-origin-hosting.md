# ADR 0001: Die API liefert den Blazor-WASM-Client aus (Same-Origin)

- Status: akzeptiert
- Datum: 2026-08-25 (Vorbereitung des Livegangs)

## Kontext

Bis zum Livegang liefen API und Blazor-WASM-Client als getrennte Prozesse auf verschiedenen Ports. Für den Betrieb hinter einem Tunnel bedeutete das zwei öffentliche Hostnamen, CORS und Cookies über Origin-Grenzen hinweg (`SameSite`, Credentials in jeder Anfrage).

## Entscheidung

- Die API (`HorizonNET.Api`) liefert den Client mit aus: Ein Prozess, ein Origin für App und Daten. Das Client-Projekt ist eine `ProjectReference` der API, seine Dateien kommen als Static Web Assets mit.
- `MapStaticAssets` bedient `_framework`, gehashte Modul-Namen und Kompression; `MapFallbackToFile("index.html")` liefert die App-Hülle für Deep-Links. `UseBlazorFrameworkFiles` wird nicht verwendet (kollidiert mit `MapStaticAssets`, 500 auf `_framework`-Dateien).
- Die App-Hülle ist öffentlich (`AllowAnonymous`), geschützt sind die Daten: Eine Fallback-Policy verlangt für jeden anderen Endpunkt eine Anmeldung (Cookie-Auth mit ASP.NET Identity).

## Konsequenzen

- Kein CORS, Cookies ohne Sonderbehandlung; die CSP kann `connect-src 'self'` setzen.
- Ein `dotnet publish` der API ergibt das komplette Deployment; das gilt für den Windows-Dienst wie für das Docker-Image.
- Client und API werden immer gemeinsam ausgeliefert und tragen dieselbe Version (`Directory.Build.props`).
- Ein `dotnet build` während des Debuggens aus Visual Studio ersetzt die Client-Dateien; der laufende Client muss danach neu starten.
