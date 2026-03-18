using System.ComponentModel.DataAnnotations;

namespace AeroponicIOT.Options;

public class ProvisioningOptions
{
    [Required]
    [MinLength(8)]
    public string SharedKey { get; set; } = string.Empty;

    [Range(1, 120)]
    public int ClaimCodeMinutes { get; set; } = 10;
}