using AeroponicIOT.DTOs;
using AeroponicIOT.Services.AI;
using AeroponicIOT.Services.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AeroponicIOT.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AISuggestionController : ControllerBase
{
    private readonly IAISuggestionService _aiSuggestionService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<AISuggestionController> _logger;

    public AISuggestionController(
        IAISuggestionService aiSuggestionService,
        ICurrentUserService currentUserService,
        ILogger<AISuggestionController> logger)
    {
        _aiSuggestionService = aiSuggestionService;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    /// <summary>
    /// Manually trigger an AI analysis for a specific device.
    /// The suggestion will be delivered as a notification to the device owner.
    /// </summary>
    [HttpPost("analyze/{deviceId}")]
    public async Task<IActionResult> AnalyzeDevice(int deviceId, CancellationToken cancellationToken)
    {
        try
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (!currentUser.UserId.HasValue)
            {
                return ApiProblem(StatusCodes.Status401Unauthorized, "Unauthorized", "User not authenticated");
            }

            // Admins can analyze any device; farmers can only analyze their own.
            if (!currentUser.IsAdministrator)
            {
                return ApiProblem(StatusCodes.Status403Forbidden, "Forbidden", "Only administrators can manually trigger AI analysis");
            }

            // The service resolves the device by ID internally; pass empty MAC for manual triggers.
            var result = await _aiSuggestionService.AnalyzeSensorDataAsync(deviceId, string.Empty, cancellationToken);

            if (result == null)
            {
                return Ok(ApiResponse.Success<object?>(null, "AI analysis completed — no actionable suggestion at this time"));
            }

            return Ok(ApiResponse.Success(result, "AI analysis complete — suggestion has been sent as notification"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error triggering AI analysis for device {DeviceId}", deviceId);
            return ApiProblem(StatusCodes.Status500InternalServerError, "Internal Server Error", "Error during AI analysis");
        }
    }

    private ObjectResult ApiProblem(int statusCode, string title, string detail)
    {
        return ProblemResponseFactory.Create(this, statusCode, title, detail);
    }
}
