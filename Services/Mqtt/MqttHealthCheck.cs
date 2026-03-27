using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AeroponicIOT.Services.Mqtt;

public sealed class MqttHealthCheck : IHealthCheck
{
    private readonly IMqttService _mqttService;

    public MqttHealthCheck(IMqttService mqttService)
    {
        _mqttService = mqttService;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!_mqttService.IsRunning)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("MQTT broker is stopped"));
        }

        if (!_mqttService.IsZigbeeBridgeReady)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "MQTT broker is running, but Zigbee bridge is not ready",
                data: new Dictionary<string, object>
                {
                    ["zigbeeBridgeReady"] = false,
                    ["zigbeeBridgeStatus"] = _mqttService.ZigbeeBridgeReadinessMessage
                }));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            "MQTT broker is running",
            data: new Dictionary<string, object>
            {
                ["zigbeeBridgeReady"] = true,
                ["zigbeeBridgeStatus"] = _mqttService.ZigbeeBridgeReadinessMessage
            }));
    }
}
