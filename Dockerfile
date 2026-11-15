# HorizonNET als Container: API + Blazor-WASM-Client in einem Prozess (Same-Origin).
#
#   docker compose up -d --build
#
# Persistente Daten liegen unter /data (Volume):
#   /data/horizonnet.db (+ -wal, -shm)  SQLite-Datenbank
#   /data/keys/                         DataProtection-Schlüsselring
# ⚠ Beides gehört zusammen ins Backup – ohne den Schlüsselring sind die verschlüsselten
#   Spalten (Journal etc.) nicht mehr lesbar.

# Feste Image-Versionen statt des wandernden Tags "10.0": Ein Rebuild liefert so
# dasselbe Ergebnis, und ein Update ist eine bewusste Änderung an genau diesen Zeilen
# (Tags: mcr.microsoft.com/v2/dotnet/sdk/tags/list bzw. …/aspnet/tags/list).
ARG SDK_VERSION=10.0.404
ARG ASPNET_VERSION=10.0.15

# ── Build ────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:${SDK_VERSION} AS build
WORKDIR /src

# Erst nur Projektdateien → Restore-Layer bleibt gecacht, solange sich keine
# Paketreferenzen ändern.
COPY Directory.Build.props Directory.Packages.props global.json nuget.config ./
COPY Shared/HorizonNET.Shared.Transfer/HorizonNET.Shared.Transfer.csproj Shared/HorizonNET.Shared.Transfer/
COPY Server/HorizonNET.Domain/HorizonNET.Domain.csproj Server/HorizonNET.Domain/
COPY Server/HorizonNET.Data/HorizonNET.Data.csproj Server/HorizonNET.Data/
COPY Server/HorizonNET.Api/HorizonNET.Api.csproj Server/HorizonNET.Api/
COPY Client/HorizonNET.App/HorizonNET.App.csproj Client/HorizonNET.App/
COPY Tools/HorizonNET.KeyMigration/HorizonNET.KeyMigration.csproj Tools/HorizonNET.KeyMigration/
RUN dotnet restore Server/HorizonNET.Api/HorizonNET.Api.csproj \
 && dotnet restore Tools/HorizonNET.KeyMigration/HorizonNET.KeyMigration.csproj

COPY Shared/ Shared/
COPY Server/ Server/
COPY Client/ Client/
COPY Tools/ Tools/
RUN dotnet publish Server/HorizonNET.Api/HorizonNET.Api.csproj -c Release -o /out/app --no-restore \
 && dotnet publish Tools/HorizonNET.KeyMigration/HorizonNET.KeyMigration.csproj -c Release -o /out/tools --no-restore

# ── Runtime ──────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:${ASPNET_VERSION} AS runtime
WORKDIR /app

# Datenordner vorab anlegen und dem unprivilegierten Benutzer "app" (UID 1654, im
# Basis-Image enthalten) geben – ein neues Named Volume übernimmt diese Rechte.
# Ein Bind-Mount (./data:/data) tut das nicht: Dort vorher "chown -R 1654:1654 ./data".
RUN mkdir -p /data/keys && chown -R app:app /data

COPY --from=build /out/app ./
# Schlüsselring-Umzugstool (z. B. für die Migration einer bestehenden Windows-Installation):
#   docker compose exec horizonnet dotnet /app/tools/HorizonNET.KeyMigration.dll verify --db /data/horizonnet.db --keys /data/keys
COPY --from=build /out/tools ./tools/

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__DefaultConnection="Data Source=/data/horizonnet.db" \
    DataProtection__KeyRingPath=/data/keys \
    TZ=Europe/Berlin

USER app
EXPOSE 8080
VOLUME ["/data"]

# Das Image hat weder curl noch wget – die API prüft sich mit --healthcheck selbst
# (GET /healthz auf dem eigenen Port, siehe Program.cs).
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD ["dotnet", "HorizonNET.Api.dll", "--healthcheck"]

ENTRYPOINT ["dotnet", "HorizonNET.Api.dll"]
