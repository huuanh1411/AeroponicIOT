using AeroponicIOT.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AeroponicIOT.Services.AI;

/// <summary>
/// Background service that periodically analyzes recent sensor data across all devices
/// and generates AI-powered suggestions. This catches insights that might be missed
/// during real-time ingestion (e.g., gradual trends, cross-device patterns).
/// </summary>
public class AIAnalysisBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<AIOptions> _aiOptions;
    private readonly ILogger<AIAnalysisBackgroundService> _logger;

    public AIAnalysisBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<AIOptions> aiOptions,
        ILogger<AIAnalysisBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _aiOptions = aiOptions;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait a bit at startup so the app can initialize fully.
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_aiOptions.Value.Enabled)
                {
                    await RunAnalysisCycleAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during AI analysis background cycle");
            }

            // Run every 15 minutes.
            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }

    private async Task RunAnalysisCycleAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();

        var aiService = scope.ServiceProvider.GetRequiredService<IAISuggestionService>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Get all devices that:
        // - Have sensor data in the last hour
        // - Are associated with a user (so we can send notifications)
        // - Have a crop assigned (so we have context for analysis)
        var activeDevices = await dbContext.Devices
            .Where(d => d.UserId.HasValue
                && d.CurrentCropId.HasValue
                && d.LastSeen != null
                && d.LastSeen >= DateTime.UtcNow.AddHours(-1))
            .Select(d => new { d.Id, d.MacAddress })
            .ToListAsync(cancellationToken);

        if (activeDevices.Count == 0)
        {
            _logger.LogDebug("No active devices found for AI analysis cycle");
            return;
        }

        _logger.LogInformation("Running AI analysis cycle for {Count} active devices", activeDevices.Count);

        foreach (var device in activeDevices)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await aiService.AnalyzeSensorDataAsync(device.Id, device.MacAddress, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI analysis failed for device {DeviceId}", device.Id);
            }

            // Small delay between devices to avoid thundering the AI API.
            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }

        _logger.LogInformation("AI analysis cycle completed");
    }
}
