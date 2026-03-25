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
        return Task.FromResult(_mqttService.IsRunning
            ? HealthCheckResult.Healthy("MQTT broker is running")
            : HealthCheckResult.Unhealthy("MQTT broker is stopped"));
    }
}
