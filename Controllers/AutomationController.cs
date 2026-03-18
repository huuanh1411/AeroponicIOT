using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AeroponicIOT.Data;
using AeroponicIOT.DTOs;
using AeroponicIOT.Models;

namespace AeroponicIOT.Controllers;

[ApiController]
[Route("api/automation")]
[Authorize]
public class AutomationController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<AutomationController> _logger;

    public AutomationController(ApplicationDbContext context, ILogger<AutomationController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get all automation rules
    /// </summary>
    [HttpGet("rules")]
    public async Task<ActionResult<IEnumerable<AutomationRule>>> GetRules()
    {
        try
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

            IQueryable<AutomationRule> query = _context.AutomationRules.Include(r => r.Device);
            if (userRole != "Administrator")
            {
                if (!int.TryParse(userIdClaim, out var userIdInt))
                    return Unauthorized();

                query = query.Where(r => r.Device != null && r.Device.UserId == userIdInt);
            }

            var rules = await query.OrderByDescending(r => r.IsActive)
                .ThenByDescending(r => r.CreatedAt)
                .ToListAsync();

            return Ok(rules);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching automation rules");
            return StatusCode(500, "Error fetching rules");
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
                return NotFound();

            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

            if (userRole != "Administrator")
            {
                if (!int.TryParse(userIdClaim, out var userIdInt) || rule.Device == null || rule.Device.UserId != userIdInt)
                {
                    return Forbid();
                }
            }

            return Ok(rule);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching rule {RuleId}", id);
            return StatusCode(500, "Error fetching rule");
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
                return BadRequest(ModelState);

            // Validate device exists
            var device = await _context.Devices.FindAsync(request.DeviceId);
            if (device == null)
                return BadRequest("Device not found");

            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

            if (userRole != "Administrator")
            {
                if (!int.TryParse(userIdClaim, out var userIdInt) || device.UserId != userIdInt)
                    return Forbid();
            }

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

            rule.CreatedAt = DateTime.UtcNow;

            _context.AutomationRules.Add(rule);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Automation rule '{RuleName}' created for device {DeviceId}", 
                rule.RuleName, rule.DeviceId);

            return CreatedAtAction(nameof(GetRule), new { id = rule.Id }, rule);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating automation rule");
            return StatusCode(500, "Error creating rule");
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
            var rule = await _context.AutomationRules.FindAsync(id);
            if (rule == null)
                return NotFound();

            var device = await _context.Devices.FindAsync(rule.DeviceId);
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

            if (userRole != "Administrator")
            {
                if (!int.TryParse(userIdClaim, out var userIdInt) || device == null || device.UserId != userIdInt)
                    return Forbid();
            }

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
            return Ok(rule);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating automation rule {RuleId}", id);
            return StatusCode(500, "Error updating rule");
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
            var rule = await _context.AutomationRules.FindAsync(id);
            if (rule == null)
                return NotFound();

            var device = await _context.Devices.FindAsync(rule.DeviceId);
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

            if (userRole != "Administrator")
            {
                if (!int.TryParse(userIdClaim, out var userIdInt) || device == null || device.UserId != userIdInt)
                    return Forbid();
            }

            rule.IsActive = !rule.IsActive;
            _context.AutomationRules.Update(rule);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Automation rule {RuleId} toggled to {Status}", id, rule.IsActive);
            return Ok(rule);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling automation rule {RuleId}", id);
            return StatusCode(500, "Error toggling rule");
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
            var rule = await _context.AutomationRules.FindAsync(id);
            if (rule == null)
                return NotFound();

            var device = await _context.Devices.FindAsync(rule.DeviceId);
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

            if (userRole != "Administrator")
            {
                if (!int.TryParse(userIdClaim, out var userIdInt) || device == null || device.UserId != userIdInt)
                    return Forbid();
            }

            _context.AutomationRules.Remove(rule);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Automation rule {RuleId} deleted", id);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting automation rule {RuleId}", id);
            return StatusCode(500, "Error deleting rule");
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
                return NotFound();

            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

            if (userRole != "Administrator")
            {
                if (!int.TryParse(userIdClaim, out var userIdInt) || device.UserId != userIdInt)
                    return Forbid();
            }

            var rules = await _context.AutomationRules
                .Where(r => r.DeviceId == deviceId)
                .OrderByDescending(r => r.Priority)
                .ToListAsync();

            return Ok(rules);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching rules for device {DeviceId}", deviceId);
            return StatusCode(500, "Error fetching rules");
        }
    }
}
