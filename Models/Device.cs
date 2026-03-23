using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AeroponicIOT.Models;

[Table("devices")]
public class Device
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("device_name")]
    [MaxLength(100)]
    public string? DeviceName { get; set; }

    [Required]
    [Column("mac_address")]
    [MaxLength(50)]
    public string MacAddress { get; set; } = string.Empty;

    [Column("current_crop_id")]
    public int? CurrentCropId { get; set; }

    [Column("garden_id")]
    public int? GardenId { get; set; }

    [Column("user_id")]
    public int? UserId { get; set; }

    [Column("status")]
    [MaxLength(20)]
    public string? Status { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("last_seen")]
    public DateTime? LastSeen { get; set; }

    [Column("crop_assigned_at")]
    public DateTime? CropAssignedAt { get; set; }

    [Column("chip_id")]
    [MaxLength(100)]
    public string? ChipId { get; set; }

    [Column("firmware_version")]
    [MaxLength(50)]
    public string? FirmwareVersion { get; set; }

    [Column("provisioned_at")]
    public DateTime? ProvisionedAt { get; set; }

    [Column("claim_code")]
    [MaxLength(16)]
    public string? ClaimCode { get; set; }

    [Column("claim_code_expires_at")]
    public DateTime? ClaimCodeExpiresAt { get; set; }

    // For backward compatibility
    [NotMapped]
    public string Name => DeviceName ?? "Unknown Device";

    [NotMapped]
    public string? Description => $"Status: {Status ?? "Unknown"}";

    [NotMapped]
    public bool IsActive => Status is not null &&
        (Status.Equals("active", StringComparison.OrdinalIgnoreCase) ||
         Status.Equals("online", StringComparison.OrdinalIgnoreCase));

    [NotMapped]
    public int? CropId => CurrentCropId;

    // Navigation properties
    [ForeignKey("CurrentCropId")]
    public Crop? Crop { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    [ForeignKey("GardenId")]
    public Garden? Garden { get; set; }

    public ICollection<SensorLog> SensorLogs { get; set; } = new List<SensorLog>();
    public ICollection<ActuatorLog> ActuatorLogs { get; set; } = new List<ActuatorLog>();
}