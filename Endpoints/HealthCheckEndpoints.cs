using AeroponicIOT.Data;
using AeroponicIOT.Services.Mqtt;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace AeroponicIOT.Endpoints;

/// <summary>
/// Health check endpoints for Kubernetes/container orchestration
/// </summary>
public static class HealthCheckEndpoints
{
    public static void MapHealthCheckEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/health").WithOpenApi();

        // Liveness probe: always returns 200 if process is alive
        group.MapGet("/live", () => Results.Ok(new { status = "alive" }))
            .WithName("Liveness")
            .WithOpenApi()
            .Produces(StatusCodes.Status200OK);

        // Readiness probe: returns 200 only if all dependencies are healthy
        group.MapGet("/ready", HandleReadinessProbe)
            .WithName("Readiness")
            .WithOpenApi()
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        // Startup probe: returns 200 after migrations complete
        group.MapGet("/startup", HandleStartupProbe)
            .WithName("Startup")
            .WithOpenApi()
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<IResult> HandleReadinessProbe(
        ApplicationDbContext dbContext,
        IConnectionMultiplexer? redis,
        IMqttService? mqttService,
        ILogger<object> logger)
    {
        var checks = new Dictionary<string, bool>();

        // Database connectivity
        try
        {
            await dbContext.Database.ExecuteSqlRawAsync("SELECT 1");
            checks["database"] = true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Database health check failed");
            checks["database"] = false;
        }

        // Redis connectivity (optional)
        if (redis != null)
        {
            try
            {
                await redis.GetServer(redis.GetEndPoints().First()).PingAsync();
                checks["redis"] = true;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Redis health check failed");
                checks["redis"] = false;
            }
        }
        else
        {
            checks["redis"] = true; // Not configured, considered healthy
        }

        // MQTT broker connectivity
        if (mqttService != null)
        {
            checks["mqtt"] = mqttService.IsRunning;
        }
        else
        {
            checks["mqtt"] = true; // Not configured, considered healthy
        }

        var allHealthy = checks.Values.All(x => x);
        var statusCode = allHealthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;
        var response = new
        {
            status = allHealthy ? "ready" : "not_ready",
            checks = checks,
            timestamp = DateTime.UtcNow
        };

        return Results.Json(response, statusCode: statusCode);
    }

    private static async Task<IResult> HandleStartupProbe(ApplicationDbContext dbContext, ILogger<object> logger)
    {
        try
        {
            // Check if migrations have been applied
            var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync();
            if (pendingMigrations.Any())
            {
                logger.LogWarning("Pending migrations detected: {Migrations}", 
                    string.Join(", ", pendingMigrations));
                var response = new
                {
                    status = "not_ready",
                    reason = "Pending database migrations",
                    pendingMigrations = pendingMigrations.ToList(),
                    timestamp = DateTime.UtcNow
                };
                return Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return Results.Json(new
            {
                status = "started",
                timestamp = DateTime.UtcNow
            }, statusCode: StatusCodes.Status200OK);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Startup health check failed");
            var response = new
            {
                status = "not_ready",
                reason = "Health check error",
                error = ex.Message,
                timestamp = DateTime.UtcNow
            };
            return Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
