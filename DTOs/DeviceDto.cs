namespace AeroponicIOT.DTOs;

public class DeviceDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
    public string? Status { get; set; }
    public int? CurrentCropId { get; set; }
    public string? CropName { get; set; }
    public int? GardenId { get; set; }
    public string? GardenName { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? LastSeen { get; set; }
    public bool IsActive { get; set; }
}

public class CreateDeviceDto
{
    public string Name { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
    public int? CurrentCropId { get; set; }
    public int? GardenId { get; set; }
}

public class UpdateDeviceDto
{
    public string? Name { get; set; }
    public int? CurrentCropId { get; set; }
    public int? GardenId { get; set; }
    public string? Status { get; set; }
}
