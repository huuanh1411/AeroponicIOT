namespace AeroponicIOT.DTOs;

public class CropStageUpsertDto
{
    public string StageName { get; set; } = string.Empty;
    public int? DayStart { get; set; }
    public int? DayEnd { get; set; }
    public decimal? PhMin { get; set; }
    public decimal? PhMax { get; set; }
    public int? PpmMin { get; set; }
    public int? PpmMax { get; set; }
    public int? WaterTempMin { get; set; }
    public int? WaterTempMax { get; set; }
    public int? HumidityMin { get; set; }
    public int? HumidityMax { get; set; }
    public int? PumpOnMinutes { get; set; }
    public int? PumpOffMinutes { get; set; }
}

public class CropUpsertDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? TotalDaysEst { get; set; }
    public List<CropStageUpsertDto> Stages { get; set; } = new();
}