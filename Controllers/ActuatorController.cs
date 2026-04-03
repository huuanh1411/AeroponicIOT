using AeroponicIOT.Data;
using AeroponicIOT.DTOs;
using AeroponicIOT.Models;
using AeroponicIOT.Services.Mqtt;
using AeroponicIOT.Services.Security;
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
    private readonly ICurrentUserService _currentUserService;

    public ActuatorController(
        ApplicationDbContext context,
        IMqttService mqttService,
        ILogger<ActuatorController> logger,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _mqttService = mqttService;
        _logger = logger;
        _currentUserService = currentUserService;
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
                return ApiProblem(StatusCodes.Status404NotFound, "Not Found", $"Device with MAC address {controlDto.MacAddress} not found");
            }

            // Ensure requester owns the device or is admin
            var currentUser = _currentUserService.GetCurrentUser();

            if (!currentUser.IsAdministrator)
            {
                if (!currentUser.UserId.HasValue || device.UserId != currentUser.UserId.Value)
                {
                    _logger.LogWarning("Unauthorized actuator control attempt by user {UserId} on device {DeviceId}", currentUser.UserId, device.Id);
                    return Forbid();
                }
            }

            // Validate action
            var normalizedAction = controlDto.Action.ToUpperInvariant();
            if (normalizedAction != "ON" && normalizedAction != "OFF")
            {
                return ApiProblem(StatusCodes.Status400BadRequest, "Bad Request", "Action must be 'ON' or 'OFF'");
            }

            // Create actuator log
            var actuatorLog = new ActuatorLog
            {
                DeviceId = device.Id,
                ActuatorType = controlDto.ActuatorType,
                Action = normalizedAction,
                Timestamp = DateTime.UtcNow
            };

            _context.ActuatorLogs.Add(actuatorLog);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Actuator {ActuatorType} {Action} for device {DeviceName} ({MacAddress})",
                controlDto.ActuatorType, controlDto.Action, device.Name, device.MacAddress);

            // Publish control command via MQTT
            var delivered = await PublishActuatorCommandViamqtt(device, controlDto);
            if (!delivered)
            {
                return ApiProblem(StatusCodes.Status503ServiceUnavailable, "Service Unavailable", "Actuator command logged but MQTT delivery failed");
            }

            var response = new
            {
                message = $"Actuator {controlDto.ActuatorType} {controlDto.Action} command sent successfully",
                actuatorLogId = actuatorLog.Id,
                deliveryMethod = "MQTT"
            };

            return Ok(ApiResponse.Success(response, "Actuator command sent"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error controlling actuator");
            return ApiProblem(StatusCodes.Status500InternalServerError, "Internal Server Error", "Error controlling actuator");
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
                return ApiProblem(StatusCodes.Status404NotFound, "Not Found", "Device not found");

            var currentUser = _currentUserService.GetCurrentUser();

            if (!currentUser.IsAdministrator)
            {
                if (!currentUser.UserId.HasValue || device.UserId != currentUser.UserId.Value)
                {
                    return Forbid();
                }
            }

            var logs = await _context.ActuatorLogs
                .Where(al => al.DeviceId == deviceId && al.Timestamp >= cutoffTime)
                .OrderByDescending(al => al.Timestamp)
                .Select(al => new ActuatorLogDto
                {
                    Id = al.Id,
                    DeviceId = al.DeviceId,
                    DeviceName = al.Device != null ? al.Device.DeviceName : null,
                    MacAddress = al.Device != null ? al.Device.MacAddress : null,
                    ActuatorType = al.ActuatorType ?? string.Empty,
                    Action = al.Action ?? string.Empty,
                    Timestamp = al.Timestamp
                })
                .ToListAsync();

            return Ok(ApiResponse.Success(logs, "Actuator logs retrieved"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving actuator logs");
            return ApiProblem(StatusCodes.Status500InternalServerError, "Internal Server Error", "Error retrieving actuator logs");
        }
    }

    private IActionResult ApiProblem(int statusCode, string title, string detail)
    {
        return ProblemResponseFactory.Create(this, statusCode, title, detail);
    }

    /// <summary>
    /// Publish actuator control command via MQTT
    /// Topic: devices/{macAddress}/control
    /// Payload: JSON with actuatorType and action
    /// </summary>
    private async Task<bool> PublishActuatorCommandViamqtt(Device device, ActuatorControlDto controlDto)
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
                action = controlDto.Action.ToUpperInvariant(),
                timestamp = DateTime.UtcNow
            };

            var jsonPayload = JsonSerializer.Serialize(payload);
            
            var delivered = await _mqttService.PublishAsync(topic, jsonPayload, retainFlag: false);
            if (!delivered)
            {
                _logger.LogWarning("Control command failed to publish via MQTT to {Topic}", topic);
                return false;
            }
            
            _logger.LogInformation("Control command published via MQTT to {Topic}", topic);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing control command via MQTT");
            return false;
        }
    }
}