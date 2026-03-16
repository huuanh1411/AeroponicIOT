using AeroponicIOT.Data;
using AeroponicIOT.DTOs;
using AeroponicIOT.Models;
using AeroponicIOT.Services.Mqtt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AeroponicIOT.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ActuatorController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IMqttService _mqttService;
    private readonly ILogger<ActuatorController> _logger;

    public ActuatorController(ApplicationDbContext context, IMqttService mqttService, ILogger<ActuatorController> logger)
    {
        _context = context;
        _mqttService = mqttService;
        _logger = logger;
    }

    [HttpPost("control")]
    public async Task<IActionResult> ControlActuator([FromBody] ActuatorControlDto controlDto)
    {
        try
        {
            // Find device by MAC address
            var device = await _context.Devices
                .FirstOrDefaultAsync(d => d.MacAddress == controlDto.MacAddress);

            if (device == null)
            {
                _logger.LogWarning("Device with MAC {MacAddress} not found", controlDto.MacAddress);
                return NotFound($"Device with MAC address {controlDto.MacAddress} not found");
            }

            // Ensure requester owns the device or is admin
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

            if (userRole != "Administrator")
            {
                if (!int.TryParse(userIdClaim, out var userIdInt) || device.UserId != userIdInt)
                {
                    _logger.LogWarning("Unauthorized actuator control attempt by user {UserId} on device {DeviceId}", userIdClaim, device.Id);
                    return Forbid();
                }
            }

            // Validate action
            if (controlDto.Action.ToUpper() != "ON" && controlDto.Action.ToUpper() != "OFF")
            {
                return BadRequest("Action must be 'ON' or 'OFF'");
            }

            // Create actuator log
            var actuatorLog = new ActuatorLog
            {
                DeviceId = device.Id,
                ActuatorType = controlDto.ActuatorType,
                Action = controlDto.Action.ToUpper(),
                Timestamp = DateTime.UtcNow
            };

            _context.ActuatorLogs.Add(actuatorLog);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Actuator {ActuatorType} {Action} for device {DeviceName} ({MacAddress})",
                controlDto.ActuatorType, controlDto.Action, device.Name, device.MacAddress);

            // Publish control command via MQTT
            await PublishActuatorCommandViamqtt(device, controlDto);

            return Ok(new
            {
                message = $"Actuator {controlDto.ActuatorType} {controlDto.Action} command sent successfully",
                actuatorLogId = actuatorLog.Id,
                deliveryMethod = "MQTT"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error controlling actuator");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("logs/{deviceId}")]
    public async Task<IActionResult> GetActuatorLogs(int deviceId, [FromQuery] int days = 7)
    {
        try
        {
            var cutoffTime = DateTime.UtcNow.AddDays(-days);

            var device = await _context.Devices.FindAsync(deviceId);
            if (device == null)
                return NotFound();

            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

            if (userRole != "Administrator")
            {
                if (!int.TryParse(userIdClaim, out var userIdInt) || device.UserId != userIdInt)
                {
                    return Forbid();
                }
            }

            var logs = await _context.ActuatorLogs
                .Where(al => al.DeviceId == deviceId && al.Timestamp >= cutoffTime)
                .Include(al => al.Device)
                .OrderByDescending(al => al.Timestamp)
                .ToListAsync();

            return Ok(logs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving actuator logs");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Publish actuator control command via MQTT
    /// Topic: devices/{macAddress}/control
    /// Payload: JSON with actuatorType and action
    /// </summary>
    private async Task PublishActuatorCommandViamqtt(Device device, ActuatorControlDto controlDto)
    {
        try
        {
            var topic = $"devices/{device.MacAddress}/control";
            
            var payload = new
            {
                deviceId = device.Id,
                deviceName = device.Name,
                macAddress = device.MacAddress,
                actuatorType = controlDto.ActuatorType,
                action = controlDto.Action.ToUpper(),
                timestamp = DateTime.UtcNow
            };

            var jsonPayload = JsonSerializer.Serialize(payload);
            
            await _mqttService.PublishAsync(topic, jsonPayload, retainFlag: true);
            
            _logger.LogInformation("Control command published via MQTT to {Topic}", topic);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing control command via MQTT");
            // Don't throw - command is already logged in database
        }
    }
}