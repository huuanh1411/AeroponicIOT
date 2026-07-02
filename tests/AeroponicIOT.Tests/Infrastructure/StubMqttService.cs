using AeroponicIOT.Services.Mqtt;

namespace AeroponicIOT.Tests.Infrastructure;

/// <summary>
/// A no-op MQTT service stub for integration tests.
/// Reports the broker as running so health checks pass without a real broker.
/// </summary>
internal sealed class StubMqttService : IMqttService
{
    public bool IsRunning => true;
    public bool IsZigbeeBridgeReady => true;
    public string ZigbeeBridgeReadinessMessage => "Zigbee bridge not required in test environment";

    public Task StartAsync() => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
    public Task<bool> PublishAsync(string topic, string payload, bool retainFlag = false) => Task.FromResult(true);
}
