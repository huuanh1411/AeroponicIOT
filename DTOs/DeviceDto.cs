namespace AeroponicIOT.DTOs;

public class DeviceDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
    public string? Status { get; set; }
    public int? CurrentCropId { get; set; }
    public string? CropName { get; set; }
    public DateTime? CropAssignedAt { get; set; }
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

public class DeviceSelfRegisterRequestDto
{
    public string MacAddress { get; set; } = string.Empty;
    public string? DeviceName { get; set; }
    public string? ChipId { get; set; }
    public string? FirmwareVersion { get; set; }
}

public class DeviceSelfRegisterResponseDto
{
    public bool Success { get; set; }
    public int DeviceId { get; set; }
    public bool AlreadyClaimed { get; set; }
    public string? ClaimCode { get; set; }
    public DateTime? ClaimCodeExpiresAt { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class ClaimDeviceRequestDto
{
    public string ClaimCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public int? CurrentCropId { get; set; }
    public int? GardenId { get; set; }
}

public class PendingDeviceDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
    public string? ChipId { get; set; }
    public string? FirmwareVersion { get; set; }
    public DateTime? LastSeen { get; set; }
    public DateTime? ProvisionedAt { get; set; }
}
