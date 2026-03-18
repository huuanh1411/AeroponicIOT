using System.ComponentModel.DataAnnotations;

namespace AeroponicIOT.Options;

public class JwtSettingsOptions
{
    [Required]
    [MinLength(32)]
    public string SecretKey { get; set; } = string.Empty;

    [Required]
    [MinLength(1)]
    public string Issuer { get; set; } = "AeroponicIOT";

    [Required]
    [MinLength(1)]
    public string Audience { get; set; } = "AeroponicIOT";

    [Range(5, 10080)]
    public int ExpirationMinutes { get; set; } = 1440;
}