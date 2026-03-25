using AeroponicIOT.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AeroponicIOT.Services.Maintenance;

public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly ApplicationDbContext _dbContext;

    public DatabaseHealthCheck(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);
        return canConnect
            ? HealthCheckResult.Healthy("Database is reachable")
            : HealthCheckResult.Unhealthy("Database is unreachable");
    }
}
