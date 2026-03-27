namespace AeroponicIOT.Services.Mqtt;

/// <summary>
/// Interface for MQTT broker functionality
/// </summary>
public interface IMqttService
{
    /// <summary>
    /// Start the MQTT broker
    /// </summary>
    Task StartAsync();

    /// <summary>
    /// Stop the MQTT broker
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Publish a message to a topic
    /// </summary>
    Task<bool> PublishAsync(string topic, string payload, bool retainFlag = false);

    /// <summary>
    /// Check if the broker is running
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Indicates whether Zigbee bridge readiness requirements are satisfied.
    /// Returns true when Zigbee bridging is disabled, ACL is disabled, or the
    /// configured bridge identity has authenticated at least once since startup.
    /// </summary>
    bool IsZigbeeBridgeReady { get; }

    /// <summary>
    /// Provides a human-readable readiness status for Zigbee bridge integration.
    /// </summary>
    string ZigbeeBridgeReadinessMessage { get; }
}
