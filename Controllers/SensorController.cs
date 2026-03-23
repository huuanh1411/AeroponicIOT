using AeroponicIOT.DTOs;
using AeroponicIOT.Options;
using AeroponicIOT.Services.Sensors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace AeroponicIOT.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SensorController : ControllerBase
{
    private readonly ISensorIngestionService _sensorIngestionService;
    private readonly ILogger<SensorController> _logger;
    private readonly ProvisioningOptions _provisioningOptions;

    public SensorController(
        ISensorIngestionService sensorIngestionService,
        ILogger<SensorController> logger,
        IOptions<ProvisioningOptions> provisioningOptions)
    {
        _sensorIngestionService = sensorIngestionService;
        _logger = logger;
        _provisioningOptions = provisioningOptions.Value;
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> ReceiveSensorData([FromBody] SensorDataDto sensorData)
    {
        try
        {
            if (!(User?.Identity?.IsAuthenticated ?? false) && !HasValidDeviceKey())
            {
                return Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Unauthorized",
                    detail: "Missing or invalid device key");
            }

            await _sensorIngestionService.ProcessSensorDataAsync(sensorData, HttpContext.RequestAborted);

            return Ok(ApiResponse.Success<object?>(null, "Sensor data received successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing sensor data");
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Error processing sensor data");
        }
    }

    private bool HasValidDeviceKey()
    {
        var configuredKey = _provisioningOptions.SharedKey;
        var providedKey = Request.Headers["X-Device-Key"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(configuredKey) || string.IsNullOrWhiteSpace(providedKey))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(providedKey),
            Encoding.UTF8.GetBytes(configuredKey));
    }
}