namespace AeroponicIOT.DTOs;

public sealed class ActuatorLogDto
{
    public int Id { get; set; }
    public int DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public string? MacAddress { get; set; }
    public string ActuatorType { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}