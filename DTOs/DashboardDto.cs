using AeroponicIOT.Models;

namespace AeroponicIOT.DTOs;

public class DeviceStatusDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
    public int? GardenId { get; set; }
    public string? GardenName { get; set; }
    public bool IsActive { get; set; }
    public DateTime? LastSeen { get; set; }
    public string? CropName { get; set; }
    public SensorDataDto? LatestSensorData { get; set; }
}

public class DashboardDto
{
    public List<DeviceStatusDto> Devices { get; set; } = new();
    public List<Alert> ActiveAlerts { get; set; } = new();
    public int TotalDevices { get; set; }
    public int ActiveDevices { get; set; }
}