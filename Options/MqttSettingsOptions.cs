using System.ComponentModel.DataAnnotations;

namespace AeroponicIOT.Options;

public class MqttSettingsOptions
{
    [Range(1, 65535)]
    public int Port { get; set; } = 1883;

    [Required]
    [MinLength(1)]
    public string Host { get; set; } = "localhost";

    public string? Username { get; set; }

    public string? Password { get; set; }
}