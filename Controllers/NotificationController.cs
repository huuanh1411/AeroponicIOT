using AeroponicIOT.Services.Notifications;
using AeroponicIOT.DTOs;
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
    private readonly IEmailService _emailService;
    private readonly ILogger<NotificationController> _logger;

    public NotificationController(INotificationService notificationService, IEmailService emailService, ILogger<NotificationController> logger)
    {
        _notificationService = notificationService;
        _emailService = emailService;
        _logger = logger;
    }

    /// <summary>
    /// Get email service health status (admin only)
    /// </summary>
    [HttpGet("email-health")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetEmailHealth([FromQuery] bool testConnectivity = true, CancellationToken cancellationToken = default)
    {
        try
        {
            var health = await _emailService.CheckHealthAsync(testConnectivity, cancellationToken);
            return Ok(ApiResponse.Success(health, "Email health check completed"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking email health");
            return ApiProblem(StatusCodes.Status500InternalServerError, "Internal Server Error", "Error checking email health");
        }
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
                return ApiProblem(StatusCodes.Status401Unauthorized, "Unauthorized", "User not authenticated");
            }

            var notifications = await _notificationService.GetUnreadNotificationsAsync(userId);
            
            return Ok(ApiResponse.Success(new
            {
                unreadCount = notifications.Count,
                notifications = notifications
            }, "Unread notifications retrieved"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving unread notifications");
            return ApiProblem(StatusCodes.Status500InternalServerError, "Internal Server Error", "Error retrieving unread notifications");
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
                return ApiProblem(StatusCodes.Status401Unauthorized, "Unauthorized", "User not authenticated");
            }

            await _notificationService.MarkAsReadAsync(notificationId, userId);

            return Ok(ApiResponse.Success<object?>(null, "Notification marked as read"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking notification as read");
            return ApiProblem(StatusCodes.Status500InternalServerError, "Internal Server Error", "Error marking notification as read");
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
                return ApiProblem(StatusCodes.Status401Unauthorized, "Unauthorized", "User not authenticated");
            }

            await _notificationService.ClearNotificationsAsync(userId);
            
            return Ok(ApiResponse.Success<object?>(null, "All notifications cleared"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing notifications");
            return ApiProblem(StatusCodes.Status500InternalServerError, "Internal Server Error", "Error clearing notifications");
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
                return ApiProblem(StatusCodes.Status401Unauthorized, "Unauthorized", "User not authenticated");
            }

            await _notificationService.SendNotificationAsync(
                userId,
                "Test Notification",
                "This is a test notification from Smart Farm IoT System.",
                NotificationType.Info
            );

            return Ok(ApiResponse.Success<object?>(null, "Test notification sent"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending test notification");
            return ApiProblem(StatusCodes.Status500InternalServerError, "Internal Server Error", "Error sending test notification");
        }
    }

    private IActionResult ApiProblem(int statusCode, string title, string detail)
    {
        return ProblemResponseFactory.Create(this, statusCode, title, detail);
    }
}
