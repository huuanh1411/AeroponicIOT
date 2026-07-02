using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AeroponicIOT.Data;
using AeroponicIOT.DTOs;
using AeroponicIOT.Models;
using AeroponicIOT.Services.AI;
using AeroponicIOT.Services.Security;

namespace AeroponicIOT.Controllers;

[ApiController]
[Route("api/automation")]
[Authorize]
public class AutomationController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<AutomationController> _logger;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAISuggestionService _aiSuggestionService;
    private readonly IResourceOwnershipService _resourceOwnership;

    public AutomationController(
        ApplicationDbContext context,
        ILogger<AutomationController> logger,
        ICurrentUserService currentUserService,
        IAISuggestionService aiSuggestionService,
        IResourceOwnershipService resourceOwnership)
    {
        _context = context;
        _logger = logger;
        _currentUserService = currentUserService;
        _aiSuggestionService = aiSuggestionService;
        _resourceOwnership = resourceOwnership;
    }

    /// <summary>
    /// Get all automation rules
    /// </summary>
    [HttpGet("rules")]
    public async Task<ActionResult<IEnumerable<AutomationRule>>> GetRules()
    {
        try
        {
            var currentUser = _currentUserService.GetCurrentUser();

            IQueryable<AutomationRule> query = _context.AutomationRules.Include(r => r.Device);
            if (!currentUser.IsAdministrator)
            {
                if (!currentUser.UserId.HasValue)
                    return ApiProblem(StatusCodes.Status401Unauthorized, "Unauthorized", "User not authenticated");

                var userId = currentUser.UserId.Value;
                query = query.Where(r => r.Device != null && r.Device.UserId == userId);
            }

            var rules = await query.OrderByDescending(r => r.IsActive)
                .ThenByDescending(r => r.CreatedAt)
                .ToListAsync();

            return Ok(ApiResponse.Success(rules, "Automation rules retrieved"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching automation rules");
            return ApiProblem(StatusCodes.Status500InternalServerError, "Internal Server Error", "Error fetching rules");
        }
    }

    /// <summary>
    /// Get rule by ID
    /// </summary>
    [HttpGet("rules/{id}")]
    public async Task<ActionResult<AutomationRule>> GetRule(int id)
    {
        try
        {
            var rule = await _context.AutomationRules
                .Include(r => r.Device)
                .FirstOrDefaultAsync(r => r.Id == id);
            if (rule == null)
                return ApiProblem(StatusCodes.Status404NotFound, "Not Found", "Rule not found");

            var currentUser = _currentUserService.GetCurrentUser();
            if (!_resourceOwnership.CanAccessAutomationRule(rule, currentUser))
                return Forbid();

            return Ok(ApiResponse.Success(rule, "Automation rule retrieved"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching rule {RuleId}", id);
            return ApiProblem(StatusCodes.Status500InternalServerError, "Internal Server Error", "Error fetching rule");
        }
    }

    /// <summary>
    /// Create a new automation rule
    /// </summary>
    [HttpPost("rules")]
    public async Task<ActionResult<AutomationRule>> CreateRule([FromBody] CreateAutomationRuleDto request)
    {
        try
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            var device = await _context.Devices.FindAsync(request.DeviceId);
            if (device == null)
                return ApiProblem(StatusCodes.Status400BadRequest, "Bad Request", "Device not found");

            var currentUser = _currentUserService.GetCurrentUser();
            if (!_resourceOwnership.CanAccessDevice(device, currentUser))
                return Forbid();

            var rule = new AutomationRule
            {
                DeviceId = request.DeviceId,
                RuleName = request.RuleName,
                RuleType = request.RuleType,
                ActuatorType = request.ActuatorType,
                Action = request.Action,
                ConditionParameter = request.ConditionParameter,
                ConditionValue = request.ConditionValue,
                ConditionOperator = request.ConditionOperator,
                ScheduleTime = request.ScheduleTime,
                ScheduleDays = request.ScheduleDays,
                DurationMinutes = request.DurationMinutes,
                Priority = request.Priority,
                IsActive = request.IsActive,
                CreatedAt = DateTime.UtcNow
            };

            _context.AutomationRules.Add(rule);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Automation rule '{RuleName}' created for device {DeviceId}", 
                rule.RuleName, rule.DeviceId);

            return CreatedAtAction(nameof(GetRule), new { id = rule.Id }, ApiResponse.Success(rule, "Automation rule created"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating automation rule");
            return ApiProblem(StatusCodes.Status500InternalServerError, "Internal Server Error", "Error creating rule");
        }
    }

    /// <summary>
    /// Update an automation rule
    /// </summary>
    [HttpPut("rules/{id}")]
    public async Task<IActionResult> UpdateRule(int id, [FromBody] UpdateAutomationRuleDto updatedRule)
    {
        try
        {
            var rule = await _context.AutomationRules.Include(r => r.Device).FirstOrDefaultAsync(r => r.Id == id);
            if (rule == null)
                return ApiProblem(StatusCodes.Status404NotFound, "Not Found", "Rule not found");

            var currentUser = _currentUserService.GetCurrentUser();
            if (!_resourceOwnership.CanAccessAutomationRule(rule, currentUser))
                return Forbid();

            rule.RuleName = updatedRule.RuleName;
            rule.RuleType = updatedRule.RuleType;
            rule.ActuatorType = updatedRule.ActuatorType;
            rule.Action = updatedRule.Action;
            rule.ConditionParameter = updatedRule.ConditionParameter;
            rule.ConditionValue = updatedRule.ConditionValue;
            rule.ConditionOperator = updatedRule.ConditionOperator;
            rule.ScheduleTime = updatedRule.ScheduleTime;
            rule.ScheduleDays = updatedRule.ScheduleDays;
            rule.DurationMinutes = updatedRule.DurationMinutes;
            rule.Priority = updatedRule.Priority;

            _context.AutomationRules.Update(rule);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Automation rule {RuleId} updated", id);
            return Ok(ApiResponse.Success(rule, "Automation rule updated"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating automation rule {RuleId}", id);
            return ApiProblem(StatusCodes.Status500InternalServerError, "Internal Server Error", "Error updating rule");
        }
    }

    /// <summary>
    /// Toggle rule active status
    /// </summary>
    [HttpPut("rules/{id}/toggle")]
    public async Task<IActionResult> ToggleRule(int id)
    {
        try
        {
            var rule = await _context.AutomationRules.Include(r => r.Device).FirstOrDefaultAsync(r => r.Id == id);
            if (rule == null)
                return ApiProblem(StatusCodes.Status404NotFound, "Not Found", "Rule not found");

            var currentUser = _currentUserService.GetCurrentUser();
            if (!_resourceOwnership.CanAccessAutomationRule(rule, currentUser))
                return Forbid();

            rule.IsActive = !rule.IsActive;
            _context.AutomationRules.Update(rule);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Automation rule {RuleId} toggled to {Status}", id, rule.IsActive);
            return Ok(ApiResponse.Success(rule, "Automation rule toggled"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling automation rule {RuleId}", id);
            return ApiProblem(StatusCodes.Status500InternalServerError, "Internal Server Error", "Error toggling rule");
        }
    }

    /// <summary>
    /// Delete an automation rule
    /// </summary>
    [HttpDelete("rules/{id}")]
    public async Task<IActionResult> DeleteRule(int id)
    {
        try
        {
            var rule = await _context.AutomationRules.Include(r => r.Device).FirstOrDefaultAsync(r => r.Id == id);
            if (rule == null)
                return ApiProblem(StatusCodes.Status404NotFound, "Not Found", "Rule not found");

            var currentUser = _currentUserService.GetCurrentUser();
            if (!_resourceOwnership.CanAccessAutomationRule(rule, currentUser))
                return Forbid();

            _context.AutomationRules.Remove(rule);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Automation rule {RuleId} deleted", id);
            return Ok(ApiResponse.Success<object?>(null, "Automation rule deleted"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting automation rule {RuleId}", id);
            return ApiProblem(StatusCodes.Status500InternalServerError, "Internal Server Error", "Error deleting rule");
        }
    }

    /// <summary>
    /// Get rules by device
    /// </summary>
    [HttpGet("rules/device/{deviceId}")]
    public async Task<ActionResult<IEnumerable<AutomationRule>>> GetRulesByDevice(int deviceId)
    {
        try
        {
            var device = await _context.Devices.FindAsync(deviceId);
            if (device == null)
                return ApiProblem(StatusCodes.Status404NotFound, "Not Found", "Device not found");

            var currentUser = _currentUserService.GetCurrentUser();
            if (!_resourceOwnership.CanAccessDevice(device, currentUser))
                return Forbid();

            var rules = await _context.AutomationRules
                .Where(r => r.DeviceId == deviceId)
                .OrderByDescending(r => r.Priority)
                .ToListAsync();

            return Ok(ApiResponse.Success(rules, "Automation rules retrieved"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching rules for device {DeviceId}", deviceId);
            return ApiProblem(StatusCodes.Status500InternalServerError, "Internal Server Error", "Error fetching rules");
        }
    }

    private ObjectResult ApiProblem(int statusCode, string title, string detail)
    {
        return ProblemResponseFactory.Create(this, statusCode, title, detail);
    }

    /// <summary>
    /// Manually trigger an AI analysis for a specific device (admin only).
    /// The suggestion will be delivered as a notification to the device owner.
    /// </summary>
    [HttpPost("analyze/{deviceId}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> AnalyzeDevice(int deviceId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _aiSuggestionService.AnalyzeSensorDataAsync(deviceId, string.Empty, cancellationToken);

            if (result == null)
                return Ok(ApiResponse.Success<object?>(null, "AI analysis completed — no actionable suggestion at this time"));

            return Ok(ApiResponse.Success(result, "AI analysis complete — suggestion has been sent as notification"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error triggering AI analysis for device {DeviceId}", deviceId);
            return ApiProblem(StatusCodes.Status500InternalServerError, "Internal Server Error", "Error during AI analysis");
        }
    }
}
