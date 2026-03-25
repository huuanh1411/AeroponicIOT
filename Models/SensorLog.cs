using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AeroponicIOT.Models;

[Table("sensor_logs")]
public class SensorLog
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("device_id")]
    public int DeviceId { get; set; }

    [Column("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [Column("ph")]
    public decimal? Ph { get; set; }

    [Column("tds_ppm")]
    public int? TdsPpm { get; set; }

    [Column("tds_raw")]
    public decimal? TdsRaw { get; set; }

    [Column("water_temp")]
    public int? WaterTemp { get; set; }

    [Column("water_temp_raw")]
    public decimal? WaterTempRaw { get; set; }

    [Column("humidity")]
    public int? Humidity { get; set; }

    [Column("humidity_raw")]
    public decimal? HumidityRaw { get; set; }

    // Optional light intensity (e.g., lux) for EC / light monitoring features.
    [Column("light_intensity")]
    public int? LightIntensity { get; set; }

    [Column("light_intensity_raw")]
    public decimal? LightIntensityRaw { get; set; }

    // For backward compatibility
    [NotMapped]
    public double? Tds => (double?)(TdsRaw ?? TdsPpm);

    [NotMapped]
    public double? WaterTemperature => (double?)(WaterTempRaw ?? WaterTemp);

    [NotMapped]
    public double? AirHumidity => (double?)(HumidityRaw ?? Humidity);

    [NotMapped]
    public double? Light => (double?)(LightIntensityRaw ?? LightIntensity);

    // Foreign key to Device
    public Device Device { get; set; } = null!;
}