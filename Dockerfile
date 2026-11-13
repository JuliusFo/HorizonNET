# HorizonNET als Container: API + Blazor-WASM-Client in einem Prozess (Same-Origin).
#
#   docker compose up -d --build
#
# Persistente Daten liegen unter /data (Volume):
#   /data/horizonnet.db (+ -wal, -shm)  SQLite-Datenbank
#   /data/keys/                         DataProtection-Schlüsselring
# ⚠ Beides gehört zusammen ins Backup – ohne den Schlüsselring sind die verschlüsselten
#   Spalten (Journal etc.) nicht mehr lesbar.

# ── Build ────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Erst nur Projektdateien → Restore-Layer bleibt gecacht, solange sich keine
# Paketreferenzen ändern.
COPY Directory.Build.props ./
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
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Datenordner vorab anlegen und dem unprivilegierten Benutzer "app" (UID 1654, im
# Basis-Image enthalten) geben – ein neues Named Volume übernimmt diese Rechte.
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

ENTRYPOINT ["dotnet", "HorizonNET.Api.dll"]
