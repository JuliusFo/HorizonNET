using HorizonNET.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HorizonNET.Api.Services;

// Gesund, solange die SQLite-Datenbank antwortet. Mehr braucht es nicht: Fehlt das
// Volume oder ist die Datei gesperrt, fällt genau das auf.
public class DatabaseHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
        await db.Database.CanConnectAsync(ct)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Datenbank nicht erreichbar.");
}
