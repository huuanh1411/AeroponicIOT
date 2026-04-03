using AeroponicIOT.Data;
using Microsoft.EntityFrameworkCore;

namespace AeroponicIOT.Services.Maintenance;

/// <summary>
/// Periodically cleans up old sensor and actuator logs based on configured retention.
/// </summary>
public class LogRetentionBackgroundService : BackgroundService
{
    private readonly ILogger<LogRetentionBackgroundService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;

    private static readonly Action<ILogger, Exception?> LogErrorCleanupFailed =
        LoggerMessage.Define(LogLevel.Error, new EventId(1, nameof(LogRetentionBackgroundService)), "Error during log retention cleanup.");

    private static readonly Action<ILogger, int, DateTime, Exception?> LogInfoSensorLogsDeleted =
        LoggerMessage.Define<int, DateTime>(LogLevel.Information, new EventId(2, nameof(RunCleanupAsync)), "Deleted {Count} sensor log rows older than {Cutoff}");

    private static readonly Action<ILogger, int, DateTime, Exception?> LogInfoActuatorLogsDeleted =
        LoggerMessage.Define<int, DateTime>(LogLevel.Information, new EventId(3, nameof(RunCleanupAsync)), "Deleted {Count} actuator log rows older than {Cutoff}");

    public LogRetentionBackgroundService(
        ILogger<LogRetentionBackgroundService> logger,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial delay after startup
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown
            }
            catch (Exception ex)
            {
                LogErrorCleanupFailed(_logger, ex);
            }

            // Run once per day
            await Task.Delay(TimeSpan.FromDays(1), stoppingToken);
        }
    }

    private async Task RunCleanupAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var sensorDays = GetRetentionDays("DataRetention:SensorLogDays", defaultDays: 90);
        var actuatorDays = GetRetentionDays("DataRetention:ActuatorLogDays", defaultDays: 180);

        var now = DateTime.UtcNow;

        if (sensorDays > 0)
        {
            var sensorCutoff = now.AddDays(-sensorDays);
            var deleted = await dbContext.SensorLogs
                .Where(sl => sl.Timestamp < sensorCutoff)
                .ExecuteDeleteAsync(cancellationToken);

            if (deleted > 0)
            {
                LogInfoSensorLogsDeleted(_logger, deleted, sensorCutoff, null);
            }
        }

        if (actuatorDays > 0)
        {
            var actuatorCutoff = now.AddDays(-actuatorDays);
            var deleted = await dbContext.ActuatorLogs
                .Where(al => al.Timestamp < actuatorCutoff)
                .ExecuteDeleteAsync(cancellationToken);

            if (deleted > 0)
            {
                LogInfoActuatorLogsDeleted(_logger, deleted, actuatorCutoff, null);
            }
        }
    }

    private int GetRetentionDays(string key, int defaultDays)
    {
        var value = _configuration[key];
        if (int.TryParse(value, out var days) && days >= 0)
        {
            return days;
        }

        return defaultDays;
    }
}

