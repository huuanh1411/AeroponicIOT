using System.ComponentModel.DataAnnotations;

namespace AeroponicIOT.Options;

public class AppUrlsOptions
{
    [Required]
    [MinLength(1)]
    public string DashboardBaseUrl { get; set; } = "http://localhost:5062";
}
