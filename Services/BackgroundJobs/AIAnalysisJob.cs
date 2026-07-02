using AeroponicIOT.Services.AI;

namespace AeroponicIOT.Services.BackgroundJobs;

/// <summary>
/// Background job for AI analysis without blocking sensor ingestion.
/// Persisted in SQL Server, automatically retried on failure.
/// </summary>
public class AIAnalysisJob
{
    private readonly IAISuggestionService _aiSuggestionService;
    private readonly ILogger<AIAnalysisJob> _logger;

    public AIAnalysisJob(
        IAISuggestionService aiSuggestionService,
        ILogger<AIAnalysisJob> logger)
    {
        _aiSuggestionService = aiSuggestionService;
        _logger = logger;
    }

    public async Task AnalyzeDeviceAsync(int deviceId, string macAddress, CancellationToken cancellationToken = default)
    {
        try
        {
            await _aiSuggestionService.AnalyzeSensorDataAsync(deviceId, macAddress, cancellationToken);
            _logger.LogInformation("AI analysis completed for device {DeviceId}", deviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI analysis failed for device {DeviceId}", deviceId);
            throw; // Hangfire will retry on exception
        }
    }
}
