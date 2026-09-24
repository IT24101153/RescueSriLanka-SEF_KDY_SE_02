using Microsoft.Extensions.Diagnostics.HealthChecks;
using RescueSriLanka.Api.Data;

namespace RescueSriLanka.Api.Services;

/// <summary>
/// Backs /health. An API that is running but cannot reach PostgreSQL is not
/// healthy, so the probe checks the connection rather than just answering.
/// </summary>
public class DatabaseHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy("PostgreSQL reachable.")
            : HealthCheckResult.Unhealthy("PostgreSQL unreachable.");
}
