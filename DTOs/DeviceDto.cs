using System.ComponentModel.DataAnnotations;

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
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [RegularExpression(@"^([0-9A-Fa-f]{2}[:-]){5}([0-9A-Fa-f]{2})$", ErrorMessage = "MAC address must use AA:BB:CC:DD:EE:FF or AA-BB-CC-DD-EE-FF format")]
    public string MacAddress { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int? CurrentCropId { get; set; }

    [Range(1, int.MaxValue)]
    public int? GardenId { get; set; }
}

public class UpdateDeviceDto
{
    [StringLength(100, MinimumLength = 1)]
    public string? Name { get; set; }

    [Range(1, int.MaxValue)]
    public int? CurrentCropId { get; set; }

    [Range(1, int.MaxValue)]
    public int? GardenId { get; set; }

    [StringLength(20)]
    [RegularExpression("(?i)^(pending|active|online|offline|inactive)$", ErrorMessage = "Status must be one of: Pending, Active, Online, Offline, Inactive")]
    public string? Status { get; set; }
}

public class DeviceSelfRegisterRequestDto
{
    [Required]
    [RegularExpression(@"^([0-9A-Fa-f]{2}[:-]){5}([0-9A-Fa-f]{2})$", ErrorMessage = "MAC address must use AA:BB:CC:DD:EE:FF or AA-BB-CC-DD-EE-FF format")]
    public string MacAddress { get; set; } = string.Empty;

    [StringLength(100)]
    public string? DeviceName { get; set; }

    [StringLength(100)]
    public string? ChipId { get; set; }

    [StringLength(50)]
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
    [Required]
    [RegularExpression(@"^[A-Z2-9]{6}$", ErrorMessage = "Claim code must be a 6-character uppercase code")]
    public string ClaimCode { get; set; } = string.Empty;

    [StringLength(100)]
    public string? Name { get; set; }

    [Range(1, int.MaxValue)]
    public int? CurrentCropId { get; set; }

    [Range(1, int.MaxValue)]
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
