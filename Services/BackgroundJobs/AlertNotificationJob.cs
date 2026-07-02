using AeroponicIOT.Services.Notifications;

namespace AeroponicIOT.Services.BackgroundJobs;

/// <summary>
/// Background job for sending alert notifications without blocking sensor ingestion.
/// </summary>
public class AlertNotificationJob
{
    private readonly INotificationService _notificationService;
    private readonly ILogger<AlertNotificationJob> _logger;

    public AlertNotificationJob(
        INotificationService notificationService,
        ILogger<AlertNotificationJob> logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task SendAlertAsync(int deviceId, string title, string message, string severity)
    {
        try
        {
            await _notificationService.SendAlertNotificationAsync(deviceId, title, message, severity);
            _logger.LogInformation("Alert notification sent for device {DeviceId}", deviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send alert notification for device {DeviceId}", deviceId);
            throw; // Hangfire will retry on exception
        }
    }
}
