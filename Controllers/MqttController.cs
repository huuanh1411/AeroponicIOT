using AeroponicIOT.Services.Mqtt;
using AeroponicIOT.DTOs;
using AeroponicIOT.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;

namespace AeroponicIOT.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "FarmerOrAdmin")]
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
            host = _mqttOptions.Host,
            tlsEnabled = _mqttOptions.EnableTls,
            tlsPort = _mqttOptions.EnableTls ? _mqttOptions.TlsPort : (int?)null,
            plaintextEndpointEnabled = !_mqttOptions.DisablePlaintextEndpoint,
            clientCertificateRequired = _mqttOptions.EnableTls && _mqttOptions.RequireClientCertificate,
            clientCertificateIssuerAllowlistCount = _mqttOptions.AllowedClientCertificateIssuers.Length,
            clientCertificateThumbprintAllowlistCount = _mqttOptions.AllowedClientCertificateThumbprints.Length,
            zigbee = new
            {
                enabled = _mqttOptions.EnableZigbee2MqttBridge,
                bridgeReady = _mqttService.IsZigbeeBridgeReady,
                bridgeStatus = _mqttService.ZigbeeBridgeReadinessMessage
            }
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
            var delivered = await _mqttService.PublishAsync(request.Topic!, request.Payload!, request.Retain);
            if (!delivered)
            {
                return ApiProblem(StatusCodes.Status503ServiceUnavailable, "Service Unavailable", "MQTT publish failed");
            }
            
            _logger.LogInformation("Published message to {Topic}", request.Topic);
            
            return Ok(ApiResponse.Success(new { topic = request.Topic }, "Message published successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing message");
            return ApiProblem(StatusCodes.Status500InternalServerError, "Internal Server Error", "Error publishing message");
        }
    }

    private ObjectResult ApiProblem(int statusCode, string title, string detail)
    {
        return ProblemResponseFactory.Create(this, statusCode, title, detail);
    }
}

public class PublishRequest
{
    [Required]
    [StringLength(256)]
    [RegularExpression(@".*\S.*", ErrorMessage = "Topic is required")]
    public string? Topic { get; set; }

    [Required]
    [StringLength(4096)]
    [RegularExpression(@".*\S.*", ErrorMessage = "Payload is required")]
    public string? Payload { get; set; }

    public bool Retain { get; set; } = false;
}
