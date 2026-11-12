using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using MixPilot.Infrastructure;

namespace MixPilot.Api;

public sealed class DatabaseHealthCheck(IServiceScopeFactory scopes) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MixPilotDbContext>();
        if (!await db.Database.CanConnectAsync(cancellationToken)) return HealthCheckResult.Unhealthy("Database unavailable.");
        return (await db.Database.GetPendingMigrationsAsync(cancellationToken)).Any()
            ? HealthCheckResult.Unhealthy("Database migrations are pending.") : HealthCheckResult.Healthy();
    }
}
