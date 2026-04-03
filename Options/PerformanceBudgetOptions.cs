using System.ComponentModel.DataAnnotations;

namespace AeroponicIOT.Options;

public class PerformanceBudgetOptions
{
    [Range(1, 60000)]
    public int DashboardLatestP95Ms { get; set; } = 300;

    [Range(1, 60000)]
    public int SensorIngestP95Ms { get; set; } = 150;
}