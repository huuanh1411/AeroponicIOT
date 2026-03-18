using System.ComponentModel.DataAnnotations;

namespace AeroponicIOT.Options;

public class EmailSettingsOptions
{
    public bool Enabled { get; set; }

    [Required]
    [MinLength(1)]
    public string SmtpHost { get; set; } = "smtp.gmail.com";

    [Range(1, 65535)]
    public int SmtpPort { get; set; } = 587;

    public string? SmtpUsername { get; set; }

    public string? SmtpPassword { get; set; }

    [Required]
    [EmailAddress]
    public string FromEmail { get; set; } = "noreply@smartfarmiot.com";

    [Required]
    [MinLength(1)]
    public string FromName { get; set; } = "Smart Farm IoT System";
}