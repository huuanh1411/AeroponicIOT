using AeroponicIOT.Data;
using AeroponicIOT.Models;
using AeroponicIOT.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace AeroponicIOT.Services.Notifications;

/// <summary>
/// Notification service for sending notifications to users via multiple channels
/// </summary>
public class NotificationService : INotificationService
{
    private readonly ApplicationDbContext _context;
    private readonly IEmailService _emailService;
    private readonly ILogger<NotificationService> _logger;
    private readonly AppUrlsOptions _appUrlsOptions;

    public NotificationService(
        ApplicationDbContext context,
        IEmailService emailService,
        ILogger<NotificationService> logger,
        IOptions<AppUrlsOptions> appUrlsOptions)
    {
        _context = context;
        _emailService = emailService;
        _logger = logger;
        _appUrlsOptions = appUrlsOptions.Value;
    }

    /// <summary>
    /// Send a notification to a user
    /// </summary>
    public async Task SendNotificationAsync(int userId, string title, string message, NotificationType type = NotificationType.Info)
    {
        try
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                _logger.LogWarning("User with ID {UserId} not found for notification", userId);
                return;
            }

            // Create dashboard notification
            var notification = new Notification
            {
                UserId = userId,
                Title = title,
                Message = message,
                Type = (int)type,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Notification created for user {UserId}: {Title}", userId, title);

            // Send email if user has email
            if (!string.IsNullOrEmpty(user.Email) && _emailService.IsConfigured)
            {
                var htmlBody = GenerateEmailHtml(title, message, (NotificationType)type);
                await _emailService.SendEmailAsync(user.Email, $"[{type}] {title}", htmlBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending notification to user {UserId}", userId);
        }
    }

    /// <summary>
    /// Send an alert notification when sensor values exceed thresholds
    /// </summary>
    public async Task SendAlertNotificationAsync(int deviceId, string title, string message, string severity)
    {
        try
        {
            var device = await _context.Devices
                .Include(d => d.User)
                .FirstOrDefaultAsync(d => d.Id == deviceId);

            if (device?.UserId == null)
            {
                _logger.LogWarning("Device {DeviceId} has no associated user", deviceId);
                return;
            }

            var notificationType = severity.ToLower(CultureInfo.InvariantCulture) switch
            {
                "high" or "critical" => NotificationType.Alert,
                "medium" or "warning" => NotificationType.Warning,
                _ => NotificationType.Info
            };

            await SendNotificationAsync(device.UserId.Value, title, message, notificationType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending alert notification for device {DeviceId}", deviceId);
        }
    }

    /// <summary>
    /// Get unread notifications for a user
    /// </summary>
    public async Task<List<NotificationDto>> GetUnreadNotificationsAsync(int userId)
    {
        try
        {
            var notifications = await _context.Notifications
                .Where(n => n.UserId == userId && !n.IsRead)
                .OrderByDescending(n => n.CreatedAt)
                .Select(n => new NotificationDto
                {
                    Id = n.Id,
                    UserId = n.UserId,
                    Title = n.Title,
                    Message = n.Message,
                    Type = (NotificationType)n.Type,
                    IsRead = n.IsRead,
                    CreatedAt = n.CreatedAt,
                    ReadAt = n.ReadAt
                })
                .ToListAsync();

            return notifications;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving unread notifications for user {UserId}", userId);
            return new List<NotificationDto>();
        }
    }

    /// <summary>
    /// <summary>
    /// Mark a notification as read if it belongs to the specified user
    /// </summary>
    public async Task MarkAsReadAsync(int notificationId, int userId)
    {
        try
        {
            var notification = await _context.Notifications.FindAsync(notificationId);
            if (notification == null)
            {
                _logger.LogWarning("Notification with ID {NotificationId} not found", notificationId);
                return;
            }

            if (notification.UserId != userId)
            {
                _logger.LogWarning("User {UserId} attempted to mark notification {NotificationId} they do not own", userId, notificationId);
                return;
            }

            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            _context.Notifications.Update(notification);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Notification {NotificationId} marked as read by user {UserId}", notificationId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking notification {NotificationId} as read", notificationId);
        }
    }

    /// <summary>
    /// Clear all notifications for a user
    /// </summary>
    public async Task ClearNotificationsAsync(int userId)
    {
        try
        {
            var deletedCount = await _context.Notifications
                .Where(n => n.UserId == userId)
                .ExecuteDeleteAsync();

            _logger.LogInformation("Cleared {Count} notifications for user {UserId}", deletedCount, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing notifications for user {UserId}", userId);
        }
    }

    /// <summary>
    /// Generate HTML email body
    /// </summary>
    private string GenerateEmailHtml(string title, string message, NotificationType type)
    {
        var dashboardUrl = _appUrlsOptions.DashboardBaseUrl.TrimEnd('/');
        var typeColor = type switch
        {
            NotificationType.Alert => "#dc3545",
            NotificationType.Warning => "#ffc107",
            NotificationType.Error => "#dc3545",
            NotificationType.Info => "#17a2b8",
            _ => "#6c757d"
        };

        return $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; background-color: #f5f5f5; }}
        .container {{ max-width: 600px; margin: 0 auto; background-color: white; padding: 20px; border-radius: 5px; }}
        .header {{ background-color: {typeColor}; color: white; padding: 10px; border-radius: 5px; margin-bottom: 15px; }}
        .content {{ margin: 15px 0; line-height: 1.6; }}
        .footer {{ color: #999; font-size: 12px; margin-top: 20px; border-top: 1px solid #ddd; padding-top: 10px; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h2 style='margin: 0;'>{title}</h2>
            <p style='margin: 5px 0 0 0; font-size: 12px;'>{type}</p>
        </div>
        <div class='content'>
            <p>{message}</p>
        </div>
        <div class='footer'>
            <p>Sent by Smart Farm IoT System</p>
            <p>Login to your dashboard to view more details: <a href='{dashboardUrl}'>{dashboardUrl}</a></p>
        </div>
    </div>
</body>
</html>";
    }
}
