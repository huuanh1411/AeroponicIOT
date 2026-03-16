using AeroponicIOT.Data;
using AeroponicIOT.DTOs;
using AeroponicIOT.Models;
using AeroponicIOT.Services.Notifications;
using Microsoft.EntityFrameworkCore;

namespace AeroponicIOT.Services.Sensors;

/// <summary>
/// Centralized service for ingesting sensor data regardless of transport (HTTP, MQTT, etc.).
/// </summary>
public class SensorIngestionService : ISensorIngestionService
{
    private readonly ApplicationDbContext _context;
    private readonly INotificationService _notificationService;
    private readonly ILogger<SensorIngestionService> _logger;

    public SensorIngestionService(
        ApplicationDbContext context,
        INotificationService notificationService,
        ILogger<SensorIngestionService> logger)
    {
        _context = context;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task ProcessSensorDataAsync(SensorDataDto sensorData, CancellationToken cancellationToken = default)
    {
        if (sensorData == null || string.IsNullOrWhiteSpace(sensorData.MacAddress))
        {
            _logger.LogWarning("Invalid sensor data received");
            throw new ArgumentException("Invalid sensor data");
        }

        // Find device by MAC address
        var device = await _context.Devices
            .Include(d => d.Crop)
            .ThenInclude(c => (c ?? new Crop()).CropStages)
            .FirstOrDefaultAsync(d => d.MacAddress == sensorData.MacAddress, cancellationToken);

        if (device == null)
        {
            _logger.LogWarning("Device with MAC {MacAddress} not found", sensorData.MacAddress);
            throw new InvalidOperationException($"Device with MAC address {sensorData.MacAddress} not found");
        }

        // Update device last seen
        device.LastSeen = DateTime.UtcNow;
        _context.Devices.Update(device);

        // Create sensor log
        var sensorLog = new SensorLog
        {
            DeviceId = device.Id,
            Ph = sensorData.Ph != null ? (decimal?)sensorData.Ph : null,
            TdsPpm = sensorData.Tds != null ? (int?)sensorData.Tds : null,
            WaterTemp = sensorData.WaterTemperature != null ? (int?)sensorData.WaterTemperature : null,
            Humidity = sensorData.AirHumidity != null ? (int?)sensorData.AirHumidity : null,
            LightIntensity = sensorData.LightIntensity != null ? (int?)sensorData.LightIntensity : null,
            Timestamp = DateTime.UtcNow
        };

        _context.SensorLogs.Add(sensorLog);

        await CheckThresholdsAndNotifyAsync(device, sensorLog, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Sensor data ingested for device {DeviceName} ({MacAddress})",
            device.Name ?? "Unknown", device.MacAddress ?? "Unknown");
    }

    private async Task CheckThresholdsAndNotifyAsync(Device device, SensorLog sensorLog, CancellationToken cancellationToken)
    {
        if (device.Crop == null)
        {
            return;
        }

        var currentStage = GetCurrentCropStage(device);
        if (currentStage == null)
        {
            return;
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

        foreach (var alert in alerts)
        {
            _context.Alerts.Add(alert);

            await _notificationService.SendAlertNotificationAsync(
                device.Id,
                alert.Message ?? "Alert",
                alert.Message ?? "An alert has been triggered",
                alert.Severity ?? "Medium");
        }
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

