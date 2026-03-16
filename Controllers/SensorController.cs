using AeroponicIOT.DTOs;
using AeroponicIOT.Services.Sensors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AeroponicIOT.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SensorController : ControllerBase
{
    private readonly ISensorIngestionService _sensorIngestionService;
    private readonly ILogger<SensorController> _logger;

    public SensorController(
        ISensorIngestionService sensorIngestionService,
        ILogger<SensorController> logger)
    {
        _sensorIngestionService = sensorIngestionService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> ReceiveSensorData([FromBody] SensorDataDto sensorData)
    {
        try
        {
            await _sensorIngestionService.ProcessSensorDataAsync(sensorData, HttpContext.RequestAborted);

            return Ok(new { message = "Sensor data received successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing sensor data");
            return StatusCode(500, "Internal server error");
        }
    }
}