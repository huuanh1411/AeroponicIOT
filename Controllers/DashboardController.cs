using AeroponicIOT.Data;
using AeroponicIOT.DTOs;
using AeroponicIOT.Models;
using AeroponicIOT.Services.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AeroponicIOT.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<DashboardController> _logger;
    private readonly ICurrentUserService _currentUserService;

    public DashboardController(
        ApplicationDbContext context,
        ILogger<DashboardController> logger,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _logger = logger;
        _currentUserService = currentUserService;
    }

    [HttpGet("latest")]
    public async Task<IActionResult> GetLatestData([FromQuery] int? gardenId = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        try
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var currentUser = _currentUserService.GetCurrentUser();

            IQueryable<Device> devicesQuery;
            if (currentUser.IsAdministrator)
            {
                devicesQuery = _context.Devices.AsNoTracking();
            }
            else if (currentUser.UserId.HasValue)
            {
                devicesQuery = _context.Devices.AsNoTracking().Where(d => d.UserId == currentUser.UserId.Value);
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
                d.Status == DeviceStatusValues.Active ||
                d.Status == DeviceStatusValues.Online ||
                d.Status == "active" ||
                d.Status == "online");

            var pagedDeviceRows = await devicesQuery
                .OrderBy(d => d.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(d => new
                {
                    Id = d.Id,
                    Name = d.DeviceName ?? "Unknown Device",
                    MacAddress = d.MacAddress,
                    GardenId = d.GardenId,
                    GardenName = d.Garden != null ? d.Garden.Name : null,
                    IsActive = d.Status == DeviceStatusValues.Active ||
                               d.Status == DeviceStatusValues.Online ||
                               d.Status == "active" ||
                               d.Status == "online",
                    LastSeen = d.LastSeen,
                    CropName = d.Crop != null ? d.Crop.Name : null
                })
                .ToListAsync();

            var pagedDeviceIds = pagedDeviceRows
                .Select(d => d.Id)
                .ToArray();
            var macAddressByDeviceId = pagedDeviceRows.ToDictionary(d => d.Id, d => d.MacAddress);

            Dictionary<int, SensorDataDto> latestSensorByDeviceId = new();
            if (pagedDeviceIds.Length > 0)
            {
                var latestSensorTimestamps = await _context.SensorLogs
                    .AsNoTracking()
                    .Where(sl => pagedDeviceIds.Contains(sl.DeviceId))
                    .GroupBy(sl => sl.DeviceId)
                    .Select(group => new
                    {
                        DeviceId = group.Key,
                        LatestTimestamp = group.Max(sl => sl.Timestamp)
                    })
                    .ToListAsync();

                var latestSensorLogs = await _context.SensorLogs
                    .AsNoTracking()
                    .Where(sl => pagedDeviceIds.Contains(sl.DeviceId))
                    .ToListAsync();

                latestSensorByDeviceId = latestSensorTimestamps
                    .Join(
                        latestSensorLogs,
                        timestamp => new { timestamp.DeviceId, timestamp.LatestTimestamp },
                        sensorLog => new { sensorLog.DeviceId, LatestTimestamp = sensorLog.Timestamp },
                        (timestamp, sensorLog) => new { sensorLog.DeviceId, SensorLog = sensorLog })
                    .GroupBy(x => x.DeviceId)
                    .ToDictionary(
                        group => group.Key,
                        group =>
                        {
                            var latest = group
                                .OrderByDescending(x => x.SensorLog.Id)
                                .First()
                                .SensorLog;

                            var macAddress = macAddressByDeviceId[latest.DeviceId];

                            return new SensorDataDto
                            {
                                MacAddress = macAddress,
                                Ph = (double?)latest.Ph,
                                Tds = latest.TdsPpm,
                                WaterTemperature = latest.WaterTemp,
                                AirHumidity = latest.Humidity,
                                LightIntensity = latest.LightIntensity
                            };
                        });
            }

            var pagedDevices = pagedDeviceRows
                .Select(d => new DeviceStatusDto
                {
                    Id = d.Id,
                    Name = d.Name,
                    MacAddress = d.MacAddress,
                    GardenId = d.GardenId,
                    GardenName = d.GardenName,
                    IsActive = d.IsActive,
                    LastSeen = d.LastSeen,
                    CropName = d.CropName,
                    LatestSensorData = latestSensorByDeviceId.TryGetValue(d.Id, out var latestSensor)
                        ? latestSensor
                        : null
                })
                .ToList();

            var activeAlertsQuery = _context.Alerts
                .AsNoTracking()
                .Where(a => !a.IsResolved)
                .AsQueryable();

            if (!currentUser.IsAdministrator && currentUser.UserId.HasValue)
            {
                activeAlertsQuery = activeAlertsQuery.Where(a => a.Device != null && a.Device.UserId == currentUser.UserId.Value);
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
            var currentUser = _currentUserService.GetCurrentUser();

            // Get user's devices or all if admin
            IQueryable<Device> devicesQuery;
            if (currentUser.IsAdministrator)
            {
                devicesQuery = _context.Devices.AsNoTracking();
            }
            else if (currentUser.UserId.HasValue)
            {
                devicesQuery = _context.Devices.AsNoTracking().Where(d => d.UserId == currentUser.UserId.Value);
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
            var activeDevices = devices.Count(d => DeviceStatusValues.IsActive(d.Status));
            var totalDevices = devices.Count;
            var deviceHealth = totalDevices > 0 ? (activeDevices * 100.0) / totalDevices : 0;

            // Check for critical alerts
            IQueryable<Alert> criticalAlertsQuery = _context.Alerts.Where(a => !a.IsResolved);

            if (!currentUser.IsAdministrator && currentUser.UserId.HasValue)
            {
                criticalAlertsQuery = criticalAlertsQuery.Where(a => a.Device != null && a.Device.UserId == currentUser.UserId.Value);
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

            var currentUser = _currentUserService.GetCurrentUser();

            // Get user's devices
            IQueryable<Device> devicesQuery;
            if (currentUser.IsAdministrator)
            {
                devicesQuery = _context.Devices.AsNoTracking();
            }
            else if (currentUser.UserId.HasValue)
            {
                devicesQuery = _context.Devices.AsNoTracking().Where(d => d.UserId == currentUser.UserId.Value);
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
            var currentUser = _currentUserService.GetCurrentUser();

            var device = await _context.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.Id == deviceId);
            if (device == null)
                return ApiProblem(StatusCodes.Status404NotFound, "Not Found", "Device not found");

            if (!currentUser.IsAdministrator)
            {
                if (!currentUser.UserId.HasValue || device.UserId != currentUser.UserId.Value)
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
        return ProblemResponseFactory.Create(this, statusCode, title, detail);
    }
}