using AeroponicIOT.Data;
using AeroponicIOT.DTOs;
using AeroponicIOT.Exceptions;
using AeroponicIOT.Models;
using AeroponicIOT.Services.AI;
using AeroponicIOT.Services.BackgroundJobs;
using AeroponicIOT.Services.Notifications;
using AeroponicIOT.Services.Security;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace AeroponicIOT.Services.Sensors;

/// <summary>
/// Centralized service for ingesting sensor data regardless of transport (HTTP, MQTT, etc.).
/// Uses Hangfire for background job processing (alerts, AI analysis).
/// </summary>
public class SensorIngestionService : ISensorIngestionService
{
    private readonly ApplicationDbContext _context;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly ILogger<SensorIngestionService> _logger;

    public SensorIngestionService(
        ApplicationDbContext context,
        IBackgroundJobClient backgroundJobClient,
        ILogger<SensorIngestionService> logger)
    {
        _context = context;
        _backgroundJobClient = backgroundJobClient;
        _logger = logger;
    }

    public async Task ProcessSensorDataAsync(SensorDataDto sensorData, CancellationToken cancellationToken = default)
    {
        if (sensorData == null || string.IsNullOrWhiteSpace(sensorData.MacAddress))
        {
            _logger.LogWarning("Invalid sensor data received");
            throw new DomainValidationException("MAC address is required");
        }

        var normalizedMac = DeviceIdentityNormalizer.NormalizeMac(sensorData.MacAddress);

        SanitizeOutOfRangeValues(sensorData, normalizedMac);

        // Find device by MAC address
        var device = await _context.Devices
            .Include(d => d.Crop)
            .ThenInclude(c => c!.CropStages)
            .FirstOrDefaultAsync(d => d.MacAddress == normalizedMac, cancellationToken);

        if (device == null)
        {
            _logger.LogWarning("Device with MAC {MacAddress} not found", normalizedMac);
            throw new ResourceNotFoundException($"Device with MAC address {normalizedMac} not found");
        }

        // Update device last seen
        device.LastSeen = DateTime.UtcNow;
        _context.Devices.Update(device);

        // Create sensor log
        var sensorLog = new SensorLog
        {
            DeviceId = device.Id,
            Ph = sensorData.Ph != null ? (decimal?)sensorData.Ph : null,
            TdsRaw = sensorData.Tds != null ? (decimal?)sensorData.Tds : null,
            TdsPpm = sensorData.Tds != null ? (int?)Math.Round(sensorData.Tds.Value, MidpointRounding.AwayFromZero) : null,
            WaterTempRaw = sensorData.WaterTemperature != null ? (decimal?)sensorData.WaterTemperature : null,
            WaterTemp = sensorData.WaterTemperature != null ? (int?)Math.Round(sensorData.WaterTemperature.Value, MidpointRounding.AwayFromZero) : null,
            HumidityRaw = sensorData.AirHumidity != null ? (decimal?)sensorData.AirHumidity : null,
            Humidity = sensorData.AirHumidity != null ? (int?)Math.Round(sensorData.AirHumidity.Value, MidpointRounding.AwayFromZero) : null,
            LightIntensityRaw = sensorData.LightIntensity != null ? (decimal?)sensorData.LightIntensity : null,
            LightIntensity = sensorData.LightIntensity != null ? (int?)Math.Round(sensorData.LightIntensity.Value, MidpointRounding.AwayFromZero) : null,
            Timestamp = DateTime.UtcNow
        };

        _context.SensorLogs.Add(sensorLog);

        var alerts = CheckThresholds(device, sensorLog);
        if (alerts.Count > 0)
        {
            _context.Alerts.AddRange(alerts);
        }

        await _context.SaveChangesAsync(cancellationToken);

        // Queue alert notifications as background jobs (non-blocking)
        foreach (var alert in alerts)
        {
            _backgroundJobClient.Enqueue<AlertNotificationJob>(job =>
                job.SendAlertAsync(
                    device.Id,
                    alert.Message ?? "Alert",
                    alert.Message ?? "An alert has been triggered",
                    alert.Severity ?? "Medium"));
        }

        // Queue AI analysis as background job (non-blocking, persistent, retryable)
        _backgroundJobClient.Enqueue<AIAnalysisJob>(job =>
            job.AnalyzeDeviceAsync(device.Id, normalizedMac, CancellationToken.None));

        _logger.LogInformation("Sensor data ingested for device {DeviceName} ({MacAddress}) — {AlertCount} alerts queued, AI analysis queued",
            device.Name ?? "Unknown", device.MacAddress ?? "Unknown", alerts.Count);
    }

    private void SanitizeOutOfRangeValues(SensorDataDto sensorData, string normalizedMac)
    {
        if (sensorData.Ph is < 0 or > 14)
        {
            _logger.LogWarning("Discarded out-of-range pH value {Value} for device {MacAddress}", sensorData.Ph, normalizedMac);
            sensorData.Ph = null;
        }

        if (sensorData.Tds is < 0 or > 50000)
        {
            _logger.LogWarning("Discarded out-of-range TDS value {Value} for device {MacAddress}", sensorData.Tds, normalizedMac);
            sensorData.Tds = null;
        }

        if (sensorData.WaterTemperature is < -20 or > 100)
        {
            _logger.LogWarning("Discarded out-of-range water temperature {Value} for device {MacAddress}", sensorData.WaterTemperature, normalizedMac);
            sensorData.WaterTemperature = null;
        }

        if (sensorData.AirHumidity is < 0 or > 100)
        {
            _logger.LogWarning("Discarded out-of-range humidity value {Value} for device {MacAddress}", sensorData.AirHumidity, normalizedMac);
            sensorData.AirHumidity = null;
        }

        if (sensorData.LightIntensity is < 0 or > 200000)
        {
            _logger.LogWarning("Discarded out-of-range light intensity {Value} for device {MacAddress}", sensorData.LightIntensity, normalizedMac);
            sensorData.LightIntensity = null;
        }
    }

    private List<Alert> CheckThresholds(Device device, SensorLog sensorLog)
    {
        if (device.Crop == null)
        {
            return new List<Alert>();
        }

        var currentStage = GetCurrentCropStage(device);
        if (currentStage == null)
        {
            return new List<Alert>();
        }

        var alerts = new List<Alert>();

        // Check pH
        if (sensorLog.Ph.HasValue)
        {
            if ((double)sensorLog.Ph < currentStage.MinPh || (double)sensorLog.Ph > currentStage.MaxPh)
            {
                alerts.Add(new Alert
                {
                    DeviceId = device.Id,
                    AlertType = "Warning",
                    Message = $"pH level {sensorLog.Ph:F2} is outside acceptable range ({currentStage.MinPh:F1}-{currentStage.MaxPh:F1})",
                    Severity = "Medium",
                    IsResolved = false
                });
            }
        }

        // Check TDS / EC
        if (sensorLog.TdsPpm.HasValue)
        {
            if (sensorLog.TdsPpm < currentStage.MinTds || sensorLog.TdsPpm > currentStage.MaxTds)
            {
                alerts.Add(new Alert
                {
                    DeviceId = device.Id,
                    AlertType = "Warning",
                    Message = $"TDS/EC level {sensorLog.TdsPpm:F0} is outside acceptable range ({currentStage.MinTds:F0}-{currentStage.MaxTds:F0})",
                    Severity = "Medium",
                    IsResolved = false
                });
            }
        }

        // Check water temperature
        if (sensorLog.WaterTemp.HasValue)
        {
            if (sensorLog.WaterTemp < currentStage.MinTemperature || sensorLog.WaterTemp > currentStage.MaxTemperature)
            {
                alerts.Add(new Alert
                {
                    DeviceId = device.Id,
                    AlertType = "Warning",
                    Message = $"Water temperature {sensorLog.WaterTemp:F1}°C is outside acceptable range ({currentStage.MinTemperature:F1}-{currentStage.MaxTemperature:F1}°C)",
                    Severity = "High",
                    IsResolved = false
                });
            }
        }

        // Check humidity
        if (sensorLog.Humidity.HasValue)
        {
            if (sensorLog.Humidity < currentStage.MinHumidity || sensorLog.Humidity > currentStage.MaxHumidity)
            {
                alerts.Add(new Alert
                {
                    DeviceId = device.Id,
                    AlertType = "Warning",
                    Message = $"Air humidity {sensorLog.Humidity:F1}% is outside acceptable range ({currentStage.MinHumidity:F1}-{currentStage.MaxHumidity:F1}%)",
                    Severity = "Low",
                    IsResolved = false
                });
            }
        }

        return alerts;
    }

    private CropStage? GetCurrentCropStage(Device device)
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
}

