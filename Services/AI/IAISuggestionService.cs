namespace AeroponicIOT.Services.AI;

/// <summary>
/// Service that analyzes sensor data using an AI model and returns actionable
/// aeroponic farming suggestions that can be delivered as notifications.
/// </summary>
public interface IAISuggestionService
{
    /// <summary>
    /// Analyze the latest sensor reading for a device and return an AI-generated suggestion.
    /// Returns null if no actionable suggestion is needed or if AI is not configured.
    /// </summary>
    /// <param name="deviceId">The device identifier.</param>
    /// <param name="macAddress">The device MAC address.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AiSuggestionResult?> AnalyzeSensorDataAsync(
        int deviceId,
        string macAddress,
        CancellationToken cancellationToken = default);
}
