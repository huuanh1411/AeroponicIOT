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
    private readonly ProvisioningOptions _provisioningOptions;

    public SensorController(
        ISensorIngestionService sensorIngestionService,
        IOptions<ProvisioningOptions> provisioningOptions)
    {
        _sensorIngestionService = sensorIngestionService;
        _provisioningOptions = provisioningOptions.Value;
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> ReceiveSensorData([FromBody] SensorDataDto sensorData)
    {
        if (!(User?.Identity?.IsAuthenticated ?? false) && !HasValidDeviceKey())
        {
            return ApiProblem(StatusCodes.Status401Unauthorized, "Unauthorized", "Missing or invalid device key");
        }

        await _sensorIngestionService.ProcessSensorDataAsync(sensorData, HttpContext.RequestAborted);

        return Ok(ApiResponse.Success<object?>(null, "Sensor data received successfully"));
    }

    private IActionResult ApiProblem(int statusCode, string title, string detail)
    {
        return ProblemResponseFactory.Create(this, statusCode, title, detail);
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