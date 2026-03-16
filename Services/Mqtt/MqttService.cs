using AeroponicIOT.DTOs;
using AeroponicIOT.Services.Sensors;
using MQTTnet;
using MQTTnet.Adapter;
using MQTTnet.Diagnostics;
using MQTTnet.Protocol;
using MQTTnet.Server;
using System.Text;
using System.Text.Json;

namespace AeroponicIOT.Services.Mqtt;

/// <summary>
/// MQTT broker service for device communication
/// Handles device connections, subscriptions, and message publishing
/// </summary>
public class MqttService : IMqttService, IDisposable
{
    private readonly ILogger<MqttService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory _scopeFactory;
    private MqttServer? _mqttServer;
    private bool _isRunning;

    public bool IsRunning => _isRunning;

    public MqttService(
        ILogger<MqttService> logger,
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _configuration = configuration;
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Start the MQTT broker on the configured port
    /// </summary>
    public async Task StartAsync()
    {
        try
        {
            var port = int.Parse(_configuration.GetSection("MqttSettings")["Port"] ?? "1883");
            var optionsBuilder = new MqttServerOptionsBuilder()
                .WithDefaultEndpointPort(port)
                .WithDefaultEndpointBoundIPAddress(System.Net.IPAddress.Any);

            // If MQTT username/password are configured, enforce connection authentication
            var mqttUser = _configuration["MqttSettings:Username"]; 
            var mqttPassword = _configuration["MqttSettings:Password"];

            var options = optionsBuilder.Build();

            var factory = new MqttFactory();
            _mqttServer = factory.CreateMqttServer(options);

            // Handle client connected
            _mqttServer.ClientConnectedAsync += async e =>
            {
                _logger.LogInformation("MQTT Client connected: {ClientId}", e.ClientId);
                await Task.CompletedTask;
            };

            // Handle client disconnected
            _mqttServer.ClientDisconnectedAsync += async e =>
            {
                _logger.LogInformation("MQTT Client disconnected: {ClientId}", e.ClientId);
                await Task.CompletedTask;
            };

            if (!string.IsNullOrWhiteSpace(mqttUser) && !string.IsNullOrWhiteSpace(mqttPassword))
            {
                _mqttServer.ValidatingConnectionAsync += async e =>
                {
                    if (string.IsNullOrWhiteSpace(e.UserName) || string.IsNullOrWhiteSpace(e.Password))
                    {
                        e.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
                        await Task.CompletedTask;
                        return;
                    }

                    if (e.UserName != mqttUser || e.Password != mqttPassword)
                    {
                        e.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
                        await Task.CompletedTask;
                        return;
                    }

                    e.ReasonCode = MqttConnectReasonCode.Success;
                    await Task.CompletedTask;
                };
            }

            // Handle incoming published messages (for sensor data ingestion)
            _mqttServer.InterceptingPublishAsync += OnInterceptingPublishAsync;

            await _mqttServer.StartAsync();
            _isRunning = true;

            _logger.LogInformation("MQTT Broker started on port {Port}", port);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting MQTT broker");
            throw;
        }
    }

    /// <summary>
    /// Stop the MQTT broker
    /// </summary>
    public async Task StopAsync()
    {
        try
        {
            if (_mqttServer != null)
            {
                await _mqttServer.StopAsync();
                _isRunning = false;
                _logger.LogInformation("MQTT Broker stopped");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping MQTT broker");
            throw;
        }
    }

    /// <summary>
    /// Publish a message to a specific topic
    /// </summary>
    public async Task PublishAsync(string topic, string payload, bool retainFlag = false)
    {
        try
        {
            if (_mqttServer == null || !_isRunning)
            {
                _logger.LogWarning("MQTT Broker not running, cannot publish to {Topic}", topic);
                return;
            }

            var applicationMessage = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithRetainFlag(retainFlag)
                .Build();

            await _mqttServer.InjectApplicationMessage(
                new InjectedMqttApplicationMessage(applicationMessage)
                {
                    SenderClientId = "ServerPublisher"
                });

            _logger.LogDebug("Published message to topic {Topic}", topic);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing message to {Topic}", topic);
        }
    }

    public void Dispose()
    {
        if (_mqttServer != null)
        {
            _mqttServer.InterceptingPublishAsync -= OnInterceptingPublishAsync;
            _mqttServer.Dispose();
        }
    }

    private async Task OnInterceptingPublishAsync(InterceptingPublishEventArgs args)
    {
        try
        {
            var topic = args.ApplicationMessage.Topic;
            if (string.IsNullOrWhiteSpace(topic))
            {
                return;
            }

            // Expect sensor messages on: devices/{macAddress}/sensor
            if (!topic.StartsWith("devices/", StringComparison.OrdinalIgnoreCase) ||
                !topic.EndsWith("/sensor", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var segments = topic.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 3)
            {
                return;
            }

            var macAddress = segments[1];

            var payloadBytes = args.ApplicationMessage.PayloadSegment;
            if (payloadBytes.Count == 0)
            {
                return;
            }

            var json = Encoding.UTF8.GetString(payloadBytes);

            SensorDataDto? sensorData;
            try
            {
                sensorData = JsonSerializer.Deserialize<SensorDataDto>(json);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize MQTT sensor payload from topic {Topic}", topic);
                return;
            }

            if (sensorData == null)
            {
                return;
            }

            // Ensure MAC from topic is applied if not present in payload.
            if (string.IsNullOrWhiteSpace(sensorData.MacAddress))
            {
                sensorData.MacAddress = macAddress;
            }

            using var scope = _scopeFactory.CreateScope();
            var ingestionService = scope.ServiceProvider.GetRequiredService<ISensorIngestionService>();

            await ingestionService.ProcessSensorDataAsync(sensorData, CancellationToken.None);

            _logger.LogInformation("Sensor data ingested via MQTT for device {MacAddress}", sensorData.MacAddress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing MQTT sensor message");
        }
    }
}
