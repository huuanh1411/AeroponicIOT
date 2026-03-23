using AeroponicIOT.Services.Mqtt;
using AeroponicIOT.DTOs;
using AeroponicIOT.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AeroponicIOT.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MqttController : ControllerBase
{
    private readonly IMqttService _mqttService;
    private readonly ILogger<MqttController> _logger;
    private readonly MqttSettingsOptions _mqttOptions;

    public MqttController(IMqttService mqttService, ILogger<MqttController> logger, IOptions<MqttSettingsOptions> mqttOptions)
    {
        _mqttService = mqttService;
        _logger = logger;
        _mqttOptions = mqttOptions.Value;
    }

    /// <summary>
    /// Check MQTT broker status
    /// </summary>
    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        var status = new
        {
            broker = "MQTT",
            running = _mqttService.IsRunning,
            port = _mqttOptions.Port,
            host = _mqttOptions.Host
        };

        return Ok(ApiResponse.Success(status, "MQTT status retrieved"));
    }

    /// <summary>
    /// Publish a message to a topic (admin only)
    /// </summary>
    [HttpPost("publish")]
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> PublishMessage([FromBody] PublishRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Topic) || string.IsNullOrWhiteSpace(request.Payload))
            {
                return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request", detail: "Topic and Payload are required");
            }

            var delivered = await _mqttService.PublishAsync(request.Topic, request.Payload, request.Retain);
            if (!delivered)
            {
                return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Service Unavailable", detail: "MQTT publish failed");
            }
            
            _logger.LogInformation("Published message to {Topic}", request.Topic);
            
            return Ok(ApiResponse.Success(new { topic = request.Topic }, "Message published successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing message");
            return Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Internal Server Error", detail: "Error publishing message");
        }
    }
}

public class PublishRequest
{
    public string? Topic { get; set; }
    public string? Payload { get; set; }
    public bool Retain { get; set; } = false;
}
