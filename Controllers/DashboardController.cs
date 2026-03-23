using AeroponicIOT.Data;
using AeroponicIOT.DTOs;
using AeroponicIOT.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AeroponicIOT.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(ApplicationDbContext context, ILogger<DashboardController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpGet("latest")]
    public async Task<IActionResult> GetLatestData([FromQuery] int? gardenId = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        try
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;

            IQueryable<Device> devicesQuery;
            if (userRole == "Administrator")
            {
                devicesQuery = _context.Devices.AsNoTracking();
            }
            else if (int.TryParse(userIdClaim, out var userIdInt))
            {
                devicesQuery = _context.Devices.AsNoTracking().Where(d => d.UserId == userIdInt);
            }
            else
            {
                return ApiProblem(StatusCodes.Status401Unauthorized, "Unauthorized", "User not authenticated");
            }

            if (gardenId.HasValue)
            {
                devicesQuery = devicesQuery.Where(d => d.GardenId == gardenId.Value);
            }

            var totalDevices = await devicesQuery.CountAsync();
            var activeDevicesCount = await devicesQuery.CountAsync(d =>
                d.Status != null && (d.Status.ToLower() == "active" || d.Status.ToLower() == "online"));

            var pagedDevices = await devicesQuery
                .OrderBy(d => d.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(d => new DeviceStatusDto
                {
                    Id = d.Id,
                    Name = d.DeviceName ?? "Unknown Device",
                    MacAddress = d.MacAddress,
                    GardenId = d.GardenId,
                    GardenName = d.Garden != null ? d.Garden.Name : null,
                    IsActive = d.Status != null && (d.Status.ToLower() == "active" || d.Status.ToLower() == "online"),
                    LastSeen = d.LastSeen,
                    CropName = d.Crop != null ? d.Crop.Name : null,
                    LatestSensorData = _context.SensorLogs
                        .Where(sl => sl.DeviceId == d.Id)
                        .OrderByDescending(sl => sl.Timestamp)
                        .Select(sl => new SensorDataDto
                        {
                            MacAddress = d.MacAddress,
                            Ph = (double?)sl.Ph,
                            Tds = sl.TdsPpm,
                            WaterTemperature = sl.WaterTemp,
                            AirHumidity = sl.Humidity,
                            LightIntensity = sl.LightIntensity
                        })
                        .FirstOrDefault()
                })
                .ToListAsync();

            var activeAlertsQuery = _context.Alerts
                .AsNoTracking()
                .Where(a => !a.IsResolved)
                .AsQueryable();

            if (userRole != "Administrator" && int.TryParse(userIdClaim, out var alertsUserId))
            {
                activeAlertsQuery = activeAlertsQuery.Where(a => a.Device != null && a.Device.UserId == alertsUserId);
            }

            if (gardenId.HasValue)
            {
                activeAlertsQuery = activeAlertsQuery.Where(a => a.Device != null && a.Device.GardenId == gardenId.Value);
            }

            var activeAlerts = await activeAlertsQuery
                .OrderByDescending(a => a.Timestamp)
                .Take(10)
                .Select(a => new Alert
                {
                    Id = a.Id,
                    DeviceId = a.DeviceId,
                    Timestamp = a.Timestamp,
                    AlertType = a.AlertType,
                    Message = a.Message,
                    Severity = a.Severity,
                    IsResolved = a.IsResolved
                })
                .ToListAsync();

            var dashboard = new DashboardDto
            {
                Devices = pagedDevices,
                ActiveAlerts = activeAlerts,
                TotalDevices = totalDevices,
                ActiveDevices = activeDevicesCount
            };

            return Ok(ApiResponse.Success(dashboard, "Dashboard data retrieved"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving dashboard data");
            return ApiProblem(StatusCodes.Status500InternalServerError, "Internal Server Error", "Error retrieving dashboard data");
        }
    }

    [HttpGet("kpi")]
    public async Task<IActionResult> GetKeyPerformanceIndicators()
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;

            // Get user's devices or all if admin
            IQueryable<Device> devicesQuery;
            if (userRole == "Administrator")
            {
                devicesQuery = _context.Devices.AsNoTracking();
            }
            else if (int.TryParse(userIdClaim, out var userIdInt))
            {
                devicesQuery = _context.Devices.AsNoTracking().Where(d => d.UserId == userIdInt);
            }
            else
            {
                return ApiProblem(StatusCodes.Status401Unauthorized, "Unauthorized", "User not authenticated");
            }

            var devices = await devicesQuery
                .Select(d => new
                {
                    d.Status,
                    LatestSensor = _context.SensorLogs
                        .Where(sl => sl.DeviceId == d.Id)
                        .OrderByDescending(sl => sl.Timestamp)
                        .Select(sl => new
                        {
                            sl.Ph,
                            Tds = sl.TdsPpm,
                            WaterTemperature = sl.WaterTemp
                        })
                        .FirstOrDefault()
                })
                .ToListAsync();

            // Calculate averages from latest sensor data
            var latestSensors = devices
                .Where(d => d.LatestSensor != null)
                .Select(d => d.LatestSensor!)
                .ToList();

            var avgPh = latestSensors
                .Select(s => s.Ph)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .DefaultIfEmpty(0.0m)
                .Average();

            var avgTds = latestSensors
                .Select(s => s.Tds)
                .Where(v => v.HasValue)
                .Select(v => (double)v!.Value)
                .DefaultIfEmpty(0.0)
                .Average();

            var avgTemp = latestSensors
                .Select(s => s.WaterTemperature)
                .Where(v => v.HasValue)
                .Select(v => (double)v!.Value)
                .DefaultIfEmpty(0.0)
                .Average();

            // Calculate system health percentage
            var activeDevices = devices.Count(d =>
                d.Status != null &&
                (d.Status.Equals("active", StringComparison.OrdinalIgnoreCase) ||
                 d.Status.Equals("online", StringComparison.OrdinalIgnoreCase)));
            var totalDevices = devices.Count;
            var deviceHealth = totalDevices > 0 ? (activeDevices * 100.0) / totalDevices : 0;

            // Check for critical alerts
            IQueryable<Alert> criticalAlertsQuery = _context.Alerts.Where(a => !a.IsResolved);

            if (userRole != "Administrator" && int.TryParse(userIdClaim, out var alertsUserId))
            {
                criticalAlertsQuery = criticalAlertsQuery.Where(a => a.Device != null && a.Device.UserId == alertsUserId);
            }

            var criticalAlerts = await criticalAlertsQuery.CountAsync();

            // Adjust health based on alerts
            var adjustedHealth = Math.Max(0, deviceHealth - (criticalAlerts * 5));

            var kpi = new
            {
                SystemHealth = Math.Round(adjustedHealth, 1),
                SystemHealthStatus = adjustedHealth >= 80 ? "Excellent" : 
                                    adjustedHealth >= 60 ? "Good" : 
                                    adjustedHealth >= 40 ? "Fair" : "Poor",
                AveragePh = Math.Round(avgPh, 2),
                AverageTds = Math.Round(avgTds, 1),
                AverageTemperature = Math.Round(avgTemp, 1),
                ActiveDevices = activeDevices,
                TotalDevices = totalDevices,
                ActiveAlerts = criticalAlerts,
                LatestUpdate = DateTime.UtcNow
            };

            return Ok(ApiResponse.Success(kpi, "KPI retrieved"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating KPI");
            return ApiProblem(StatusCodes.Status500InternalServerError, "Internal Server Error", "Error calculating KPI");
        }
    }

    [HttpGet("health")]
    public async Task<IActionResult> GetDeviceHealth([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        try
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;

            // Get user's devices
            IQueryable<Device> devicesQuery;
            if (userRole == "Administrator")
            {
                devicesQuery = _context.Devices.AsNoTracking();
            }
            else if (int.TryParse(userIdClaim, out var userIdInt))
            {
                devicesQuery = _context.Devices.AsNoTracking().Where(d => d.UserId == userIdInt);
            }
            else
            {
                return ApiProblem(StatusCodes.Status401Unauthorized, "Unauthorized", "User not authenticated");
            }

            var devices = await devicesQuery
                .OrderBy(d => d.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(d => new
                {
                    d.Id,
                    Name = d.DeviceName ?? "Unknown Device",
                    d.LastSeen,
                    LatestSensor = _context.SensorLogs
                        .Where(sl => sl.DeviceId == d.Id)
                        .OrderByDescending(sl => sl.Timestamp)
                        .Select(sl => new
                        {
                            sl.Ph,
                            Tds = sl.TdsPpm,
                            WaterTemperature = sl.WaterTemp
                        })
                        .FirstOrDefault()
                })
                .ToListAsync();

            var deviceHealth = devices.Select(d =>
            {
                // Determine health status based on latest sensor data and activity
                var lastSensor = d.LatestSensor;
                var lastSeenMinutesAgo = d.LastSeen.HasValue 
                    ? (DateTime.UtcNow - d.LastSeen.Value).TotalMinutes 
                    : double.MaxValue;

                string status = "offline";
                if (lastSeenMinutesAgo < 5)
                {
                    status = lastSensor != null && 
                             lastSensor.Ph >= 5 && lastSensor.Ph <= 8 &&
                             lastSensor.Tds >= 400 && lastSensor.Tds <= 3000
                        ? "healthy"
                        : "warning";
                }
                else if (lastSeenMinutesAgo < 30)
                {
                    status = "warning";
                }

                return new
                {
                    d.Id,
                    d.Name,
                    Status = status,
                    LastSeen = d.LastSeen,
                    MinutesSinceActive = (int)Math.Min(lastSeenMinutesAgo, int.MaxValue),
                    LatestPh = lastSensor?.Ph,
                    LatestTds = lastSensor?.Tds,
                    LatestTemp = lastSensor?.WaterTemperature
                };
            }).ToList();

            return Ok(ApiResponse.Success(deviceHealth, "Device health retrieved"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting device health");
            return ApiProblem(StatusCodes.Status500InternalServerError, "Internal Server Error", "Error getting device health");
        }
    }

    [HttpGet("history/{deviceId}")]
    public async Task<IActionResult> GetDeviceHistory(int deviceId, [FromQuery] int hours = 24)
    {
        try
        {
            var cutoffTime = DateTime.UtcNow.AddHours(-hours);

            // Ensure the requesting user owns the device or is an admin
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;

            var device = await _context.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.Id == deviceId);
            if (device == null)
                return ApiProblem(StatusCodes.Status404NotFound, "Not Found", "Device not found");

            if (userRole != "Administrator")
            {
                if (!int.TryParse(userIdClaim, out var userIdInt) || device.UserId != userIdInt)
                {
                    return Forbid();
                }
            }

            var sensorLogs = await _context.SensorLogs
                .AsNoTracking()
                .Where(sl => sl.DeviceId == deviceId && sl.Timestamp >= cutoffTime)
                .OrderBy(sl => sl.Timestamp)
                .ToListAsync();

            return Ok(ApiResponse.Success(sensorLogs, "Device history retrieved"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving device history");
            return ApiProblem(StatusCodes.Status500InternalServerError, "Internal Server Error", "Error retrieving device history");
        }
    }

    private IActionResult ApiProblem(int statusCode, string title, string detail)
    {
        return Problem(statusCode: statusCode, title: title, detail: detail);
    }
}