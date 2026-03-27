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

    public bool RequireClientAuthentication { get; set; } = true;

    public bool EnableTls { get; set; }

    [Range(1, 65535)]
    public int TlsPort { get; set; } = 8883;

    public bool DisablePlaintextEndpoint { get; set; }

    public string? ServerCertificatePath { get; set; }

    public string? ServerCertificatePassword { get; set; }

    public bool RequireClientCertificate { get; set; }

    public string[] AllowedClientCertificateIssuers { get; set; } = Array.Empty<string>();

    public string[] AllowedClientCertificateThumbprints { get; set; } = Array.Empty<string>();

    public bool EnforceTopicAcl { get; set; } = true;

    [Required]
    [MinLength(1)]
    public string DeviceTopicPrefix { get; set; } = "devices";

    /// <summary>
    /// Enable the Zigbee2MQTT bridge integration. When true the broker
    /// subscribes to <see cref="Zigbee2MqttTopicPrefix"/>/ topics and
    /// translates ZCL payloads into sensor readings.
    /// </summary>
    public bool EnableZigbee2MqttBridge { get; set; } = false;

    /// <summary>
    /// Root topic prefix that Zigbee2MQTT publishes to (default: "zigbee2mqtt").
    /// </summary>
    [MinLength(1)]
    public string Zigbee2MqttTopicPrefix { get; set; } = "zigbee2mqtt";
}