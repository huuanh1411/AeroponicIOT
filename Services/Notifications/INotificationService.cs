namespace AeroponicIOT.Services.Notifications;

public enum NotificationType
{
    Alert,
    Warning,
    Info,
    Error
}

public enum NotificationChannel
{
    Email,
    Dashboard,
    Push
}

/// <summary>
/// Interface for sending notifications to users
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Send a notification to a user
    /// </summary>
    Task SendNotificationAsync(int userId, string title, string message, NotificationType type = NotificationType.Info);

    /// <summary>
    /// Send an alert notification
    /// </summary>
    Task SendAlertNotificationAsync(int deviceId, string title, string message, string severity);

    /// <summary>
    /// Get unread notifications for a user
    /// </summary>
    Task<List<NotificationDto>> GetUnreadNotificationsAsync(int userId);

    /// <summary>
    /// Mark notification as read (only if it belongs to the user)
    /// </summary>
    Task MarkAsReadAsync(int notificationId, int userId);

    /// <summary>
    /// Clear all notifications for a user
    /// </summary>
    Task ClearNotificationsAsync(int userId);
}

public class NotificationDto
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public string? Title { get; set; }
    public string? Message { get; set; }
    public NotificationType Type { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}
