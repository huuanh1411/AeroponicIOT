using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AeroponicIOT.Data;
using AeroponicIOT.Models;
using AeroponicIOT.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace AeroponicIOT.Services.AI;

/// <summary>
/// Analyzes sensor data using an OpenAI-compatible AI API and returns actionable
/// aeroponic farming suggestions. Integrates with the notification system so that
/// suggestions are delivered to the device owner.
/// </summary>
public class AISuggestionService : IAISuggestionService
{
    private readonly ApplicationDbContext _context;
    private readonly INotificationService _notificationService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDistributedCache _cache;
    private readonly AIOptions _options;
    private readonly ILogger<AISuggestionService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // Default system prompt — aeroponic expert that returns structured JSON.
    private const string DefaultSystemPrompt = """
You are an expert aeroponic farming advisor. Given the sensor readings, crop details,
and optimal ranges for the current growth stage, provide a concise, actionable suggestion.

Analyze the following:
1. Whether any readings are outside optimal ranges and by how much
2. Trends from recent readings (improving, worsening, stable)
3. The specific growth stage and what the plant needs at this stage
4. Actionable advice (e.g., adjust pH, add nutrients, change pump timing)

Respond with a JSON object (no markdown, no code fences):
{
  "title": "Short actionable title (max 80 chars)",
  "message": "Detailed suggestion with reasoning and specific actions (2-4 sentences)",
  "type": "Info" | "Warning" | "Alert" | "Error",
  "suggested_action": "Optional action like 'pump:ON' or 'pump:OFF' or null",
  "confidence": 0-100
}

If all readings are within optimal ranges and no action is needed, respond with:
{"title": "All parameters normal", "message": "", "type": "Info", "suggested_action": null, "confidence": 0}
""";

    public AISuggestionService(
        ApplicationDbContext context,
        INotificationService notificationService,
        IHttpClientFactory httpClientFactory,
        IDistributedCache cache,
        IOptions<AIOptions> options,
        ILogger<AISuggestionService> logger)
    {
        _context = context;
        _notificationService = notificationService;
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AiSuggestionResult?> AnalyzeSensorDataAsync(
        int deviceId,
        string macAddress,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("AI suggestions are disabled; skipping analysis for device {DeviceId}", deviceId);
            return null;
        }

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogWarning("AI suggestions are enabled but no API key is configured");
            return null;
        }

        // 1. Check cooldown — skip if we recently generated a suggestion for this device.
        var cacheKey = $"ai_suggestion_cooldown:{deviceId}";
        var cached = await _cache.GetStringAsync(cacheKey, cancellationToken);
        if (!string.IsNullOrEmpty(cached))
        {
            _logger.LogDebug("Skipping AI suggestion for device {DeviceId} (cooldown active)", deviceId);
            return null;
        }

        // 2. Gather context: device, crop, crop stages, latest sensor reading, recent trend.
        var device = await _context.Devices
            .Include(d => d.Crop)
                .ThenInclude(c => c!.CropStages)
            .Include(d => d.Garden)
            .Include(d => d.User)
            .FirstOrDefaultAsync(d => d.Id == deviceId, cancellationToken);

        if (device == null)
        {
            _logger.LogWarning("Device {DeviceId} not found for AI analysis", deviceId);
            return null;
        }

        var latestReading = await _context.SensorLogs
            .Where(sl => sl.DeviceId == deviceId)
            .OrderByDescending(sl => sl.Timestamp)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestReading == null)
        {
            _logger.LogDebug("No sensor readings yet for device {DeviceId}; skipping AI analysis", deviceId);
            return null;
        }

        // 3. Get recent readings for trend analysis (last 6 readings).
        var recentReadings = await _context.SensorLogs
            .Where(sl => sl.DeviceId == deviceId)
            .OrderByDescending(sl => sl.Timestamp)
            .Take(6)
            .OrderBy(sl => sl.Timestamp)
            .ToListAsync(cancellationToken);

        // 4. Determine current crop stage and optimal ranges.
        var currentStage = GetCurrentCropStage(device);
        var optimalRanges = BuildOptimalRangesDescription(currentStage);

        // 5. Build the AI prompt.
        var prompt = BuildPrompt(device, latestReading, recentReadings, optimalRanges);

        // 6. Call the AI API.
        AiSuggestionResult? result;
        try
        {
            result = await CallAiApiAsync(prompt, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI API call failed for device {DeviceId}", deviceId);
            return null;
        }

        if (result == null || string.IsNullOrEmpty(result.Message) || result.Confidence <= 0)
        {
            _logger.LogDebug("AI returned no actionable suggestion for device {DeviceId}", deviceId);
            return null;
        }

        // 7. Set cooldown so we don't spam the same device.
        var cacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_options.CooldownMinutes)
        };
        await _cache.SetStringAsync(cacheKey, result.Title, cacheOptions, cancellationToken);

        // 8. Deliver as notification to the device owner.
        if (device.UserId.HasValue)
        {
            try
            {
                await _notificationService.SendNotificationAsync(
                    device.UserId.Value,
                    result.Title,
                    result.Message,
                    result.Type);
                _logger.LogInformation(
                    "AI suggestion delivered for device {DeviceId}: {Title}",
                    deviceId, result.Title);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deliver AI suggestion notification for device {DeviceId}", deviceId);
            }
        }

        return result;
    }

    /// <summary>
    /// Build the prompt sent to the AI model with context about the device and readings.
    /// </summary>
    private static string BuildPrompt(
        Device device,
        SensorLog latestReading,
        List<SensorLog> recentReadings,
        string optimalRanges)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"## Device: {device.DeviceName ?? "Unknown"} ({device.MacAddress})");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Garden: {device.Garden?.Name ?? "Not assigned"}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Crop: {device.Crop?.Name ?? "Not assigned"}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Days since crop assigned: {GetCropAgeDays(device)}");

        sb.AppendLine();
        sb.AppendLine("## Optimal Ranges for Current Stage");
        sb.AppendLine(optimalRanges);

        sb.AppendLine();
        sb.AppendLine("## Latest Sensor Reading");
        sb.AppendLine(CultureInfo.InvariantCulture, $"pH: {latestReading.Ph?.ToString("F2", CultureInfo.InvariantCulture) ?? "N/A"}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"TDS: {latestReading.TdsPpm?.ToString(CultureInfo.InvariantCulture) ?? "N/A"} ppm");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Water Temperature: {latestReading.WaterTemp?.ToString(CultureInfo.InvariantCulture) ?? "N/A"}°C");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Humidity: {latestReading.Humidity?.ToString(CultureInfo.InvariantCulture) ?? "N/A"}%");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Light Intensity: {latestReading.LightIntensity?.ToString(CultureInfo.InvariantCulture) ?? "N/A"} lux");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Timestamp: {latestReading.Timestamp:O}");

        if (recentReadings.Count >= 2)
        {
            sb.AppendLine();
            sb.AppendLine("## Recent Reading Trends (oldest to newest)");
            foreach (var reading in recentReadings)
            {
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"- [{reading.Timestamp:yyyy-MM-dd HH:mm}] pH={reading.Ph?.ToString("F2", CultureInfo.InvariantCulture) ?? "N/A"}, " +
                    $"TDS={reading.TdsPpm?.ToString(CultureInfo.InvariantCulture) ?? "N/A"}, " +
                    $"Temp={reading.WaterTemp?.ToString(CultureInfo.InvariantCulture) ?? "N/A"}°C, " +
                    $"Hum={reading.Humidity?.ToString(CultureInfo.InvariantCulture) ?? "N/A"}%");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Based on the above data, provide a concise aeroponic farming suggestion.");
        sb.AppendLine("If all parameters are within range and no action is needed, return confidence 0.");

        return sb.ToString();
    }

    /// <summary>
    /// Call the OpenAI-compatible API and parse the structured response.
    /// </summary>
    private async Task<AiSuggestionResult?> CallAiApiAsync(string prompt, CancellationToken cancellationToken)
    {
        var httpClient = string.IsNullOrWhiteSpace(_options.ProxyUrl)
            ? _httpClientFactory.CreateClient()
            : _httpClientFactory.CreateClient("ai-proxy");

        if (!string.IsNullOrWhiteSpace(_options.ProxyUrl))
        {
            httpClient.BaseAddress = new Uri(_options.ProxyUrl.TrimEnd('/') + "/");
        }

        var requestBody = new
        {
            model = _options.Model,
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = _options.SystemPromptOverride ?? DefaultSystemPrompt
                },
                new
                {
                    role = "user",
                    content = prompt
                }
            },
            max_tokens = _options.MaxTokens,
            temperature = _options.Temperature,
            response_format = new { type = "json_object" }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(requestBody, JsonOptions),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogDebug("AI API response: {Response}", responseBody);

        // Parse the outer OpenAI response wrapper.
        using var jsonDoc = JsonDocument.Parse(responseBody);
        var root = jsonDoc.RootElement;

        // Extract the content from the first choice.
        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
        {
            _logger.LogWarning("AI API returned no choices");
            return null;
        }

        var messageElement = choices[0].GetProperty("message");
        var content = messageElement.GetProperty("content").GetString();

        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        // Parse the JSON content into our result type.
        try
        {
            var result = JsonSerializer.Deserialize<AiSuggestionResult>(content, JsonOptions);
            return result;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse AI suggestion JSON: {Content}", content);
            return null;
        }
    }

    /// <summary>
    /// Determine the current crop stage for the device.
    /// </summary>
    private static CropStage? GetCurrentCropStage(Device device)
    {
        if (device.Crop?.CropStages == null || device.Crop.CropStages.Count == 0)
        {
            return null;
        }

        var orderedStages = device.Crop.CropStages
            .OrderBy(cs => cs.DayStart ?? int.MaxValue)
            .ToList();

        var cropAssignedAt = device.CropAssignedAt ?? device.CreatedAt ?? DateTime.UtcNow;
        var cycleDay = Math.Max(1, (int)Math.Floor((DateTime.UtcNow - cropAssignedAt).TotalDays) + 1);

        var matchedStage = orderedStages.FirstOrDefault(stage =>
            (!stage.DayStart.HasValue || cycleDay >= stage.DayStart.Value) &&
            (!stage.DayEnd.HasValue || cycleDay <= stage.DayEnd.Value));

        return matchedStage ?? orderedStages.LastOrDefault();
    }

    /// <summary>
    /// Build a human-readable description of optimal ranges for the current stage.
    /// </summary>
    private static string BuildOptimalRangesDescription(CropStage? stage)
    {
        if (stage == null)
        {
            return "No crop stage data available. Use general aeroponic guidelines.";
        }

        return $"Stage: {stage.StageName ?? "Current Stage"} (Days {stage.DayStart ?? 0}-{stage.DayEnd ?? 0})\n" +
            $"pH: {stage.PhMin ?? 0:F1} - {stage.PhMax ?? 0:F1}\n" +
            $"TDS: {stage.PpmMin ?? 0} - {stage.PpmMax ?? 0} ppm\n" +
            $"Water Temperature: {stage.WaterTempMin ?? 0}°C - {stage.WaterTempMax ?? 0}°C\n" +
            $"Humidity: {stage.HumidityMin ?? 0}% - {stage.HumidityMax ?? 0}%";
    }

    /// <summary>
    /// Calculate days since crop was assigned to the device.
    /// </summary>
    private static int GetCropAgeDays(Device device)
    {
        var start = device.CropAssignedAt ?? device.CreatedAt ?? DateTime.UtcNow;
        return Math.Max(1, (int)(DateTime.UtcNow - start).TotalDays);
    }
}
