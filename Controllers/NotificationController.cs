using AeroponicIOT.Services.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AeroponicIOT.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationController : ControllerBase
{
    private readonly INotificationService _notificationService;
    private readonly ILogger<NotificationController> _logger;

    public NotificationController(INotificationService notificationService, ILogger<NotificationController> logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    /// <summary>
    /// Get unread notifications for the current user
    /// </summary>
    [HttpGet("unread")]
    public async Task<IActionResult> GetUnreadNotifications()
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
            {
                return Unauthorized(new { message = "User not authenticated" });
            }

            var notifications = await _notificationService.GetUnreadNotificationsAsync(userId);
            
            return Ok(new
            {
                unreadCount = notifications.Count,
                notifications = notifications
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving unread notifications");
            return StatusCode(500, new { message = "Internal server error" });
        }
    }

    /// <summary>
    /// Mark a notification as read
    /// </summary>
    [HttpPost("{notificationId}/read")]
    public async Task<IActionResult> MarkAsRead(int notificationId)
    {
        try
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
            {
                return Unauthorized(new { message = "User not authenticated" });
            }

            await _notificationService.MarkAsReadAsync(notificationId, userId);

            return Ok(new { message = "Notification marked as read" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking notification as read");
            return StatusCode(500, new { message = "Internal server error" });
        }
    }

    /// <summary>
    /// Clear all notifications for the current user
    /// </summary>
    [HttpDelete("clear")]
    public async Task<IActionResult> ClearNotifications()
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
            {
                return Unauthorized(new { message = "User not authenticated" });
            }

            await _notificationService.ClearNotificationsAsync(userId);
            
            return Ok(new { message = "All notifications cleared" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing notifications");
            return StatusCode(500, new { message = "Internal server error" });
        }
    }

    /// <summary>
    /// Test email notification (admin only)
    /// </summary>
    [HttpPost("test-email")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> TestEmailNotification()
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
            {
                return Unauthorized(new { message = "User not authenticated" });
            }

            await _notificationService.SendNotificationAsync(
                userId,
                "Test Notification",
                "This is a test notification from Smart Farm IoT System.",
                NotificationType.Info
            );

            return Ok(new { message = "Test notification sent" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending test notification");
            return StatusCode(500, new { message = "Internal server error" });
        }
    }
}
