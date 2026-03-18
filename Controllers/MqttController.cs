using AeroponicIOT.Services.Mqtt;
using Microsoft.AspNetCore.Mvc;

namespace AeroponicIOT.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MqttController : ControllerBase
{
    private readonly IMqttService _mqttService;
    private readonly ILogger<MqttController> _logger;

    public MqttController(IMqttService mqttService, ILogger<MqttController> logger)
    {
        _mqttService = mqttService;
        _logger = logger;
    }

    /// <summary>
    /// Check MQTT broker status
    /// </summary>
    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return Ok(new
        {
            broker = "MQTT",
            running = _mqttService.IsRunning,
            port = 1883,
            host = "localhost"
        });
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
                return BadRequest("Topic and Payload are required");
            }

            var delivered = await _mqttService.PublishAsync(request.Topic, request.Payload, request.Retain);
            if (!delivered)
            {
                return StatusCode(503, new { message = "MQTT publish failed", topic = request.Topic });
            }
            
            _logger.LogInformation("Published message to {Topic}", request.Topic);
            
            return Ok(new { message = "Message published successfully", topic = request.Topic });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing message");
            return StatusCode(500, new { message = "Internal server error" });
        }
    }
}

public class PublishRequest
{
    public string? Topic { get; set; }
    public string? Payload { get; set; }
    public bool Retain { get; set; } = false;
}
