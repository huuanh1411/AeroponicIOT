using AeroponicIOT.Data;
using AeroponicIOT.DTOs;
using AeroponicIOT.Models;
using AeroponicIOT.Options;
using AeroponicIOT.Services.Sensors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace AeroponicIOT.Services.Mqtt;

/// <summary>
/// MQTT client service for connecting to external MQTT broker (EMQX)
/// Handles device subscriptions, message publishing, and sensor data ingestion
/// </summary>
public class MqttService : IMqttService, IDisposable
{
    private readonly ILogger<MqttService> _logger;
    private readonly MqttSettingsOptions _mqttOptions;
    private readonly ProvisioningOptions _provisioningOptions;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConcurrentDictionary<string, string> _deviceSubscriptions = new();
    private IMqttClient? _mqttClient;
    private bool _isRunning;
    private volatile bool _zigbeeBridgeAuthenticatedSinceStartup;

    public bool IsRunning => _isRunning;

    public bool IsZigbeeBridgeReady => !_mqttOptions.EnableZigbee2MqttBridge
        || !_mqttOptions.EnforceZigbeeTopicAcl
        || _zigbeeBridgeAuthenticatedSinceStartup;

    public string ZigbeeBridgeReadinessMessage
    {
        get
        {
            if (!_mqttOptions.EnableZigbee2MqttBridge)
                return "Zigbee bridge integration is disabled";

            if (!_mqttOptions.EnforceZigbeeTopicAcl)
                return "Zigbee bridge ACL is disabled";

            return _zigbeeBridgeAuthenticatedSinceStartup
                ? "Configured Zigbee bridge identity has authenticated"
                : "Waiting for configured Zigbee bridge identity to authenticate";
        }
    }

    public MqttService(
        ILogger<MqttService> logger,
        IOptions<MqttSettingsOptions> mqttOptions,
        IOptions<ProvisioningOptions> provisioningOptions,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _mqttOptions = mqttOptions.Value;
        _provisioningOptions = provisioningOptions.Value;
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Connect to external MQTT broker (EMQX)
    /// </summary>
    public async Task StartAsync()
    {
        try
        {
            var factory = new MqttFactory();
            _mqttClient = factory.CreateMqttClient();

            // Configure connection options
            var optionsBuilder = new MqttClientOptionsBuilder()
                .WithTcpServer(_mqttOptions.Host, _mqttOptions.Port)
                .WithClientId($"AeroponicIOT-{Guid.NewGuid().ToString().Substring(0, 8)}")
                .WithCleanSession(true)
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(60));

            // Add credentials if configured
            if (!string.IsNullOrWhiteSpace(_mqttOptions.Username) && !string.IsNullOrWhiteSpace(_mqttOptions.Password))
            {
                optionsBuilder.WithCredentials(_mqttOptions.Username, _mqttOptions.Password);
            }

            // Add TLS if configured
            if (_mqttOptions.EnableTls)
            {
                optionsBuilder.WithTlsOptions(o =>
                {
                    o.WithAllowUntrustedCertificates();
                    o.WithIgnoreCertificateChainErrors();
                });
            }

            var options = optionsBuilder.Build();

            // Setup message received handler
            _mqttClient.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
            _mqttClient.DisconnectedAsync += OnDisconnectedAsync;
            _mqttClient.ConnectedAsync += OnConnectedAsync;

            // Connect to broker
            var result = await _mqttClient.ConnectAsync(options);

            if (result.ResultCode == MqttClientConnectResultCode.Success)
            {
                _isRunning = true;
                _logger.LogInformation(
                    "Connected to MQTT broker at {Host}:{Port}",
                    _mqttOptions.Host,
                    _mqttOptions.Port);

                // Subscribe to device topics
                var topicPrefix = _mqttOptions.DeviceTopicPrefix.Trim().Trim('/');
                await _mqttClient.SubscribeAsync(new MqttTopicFilterBuilder()
                    .WithTopic($"{topicPrefix}/+/telemetry")
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build());

                _logger.LogInformation("Subscribed to sensor telemetry topics");

                // Subscribe to Zigbee bridge if enabled
                if (_mqttOptions.EnableZigbee2MqttBridge)
                {
                    var zigbeePrefix = _mqttOptions.Zigbee2MqttTopicPrefix.Trim().Trim('/');
                    await _mqttClient.SubscribeAsync(new MqttTopicFilterBuilder()
                        .WithTopic($"{zigbeePrefix}/bridge/state")
                        .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                        .Build());

                    _logger.LogInformation("Subscribed to Zigbee bridge status");
                }
            }
            else
            {
                _logger.LogError(
                    "Failed to connect to MQTT broker: {ReasonCode}",
                    result.ResultCode);
                throw new InvalidOperationException($"Failed to connect to MQTT broker: {result.ResultCode}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting MQTT client connection");
            throw;
        }
    }

    /// <summary>
    /// Disconnect from the MQTT broker
    /// </summary>
    public async Task StopAsync()
    {
        try
        {
            if (_mqttClient?.IsConnected == true)
            {
                await _mqttClient.DisconnectAsync();
                _isRunning = false;
                _logger.LogInformation("Disconnected from MQTT broker");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping MQTT client connection");
            throw;
        }
    }

    /// <summary>
    /// Publish a message to a specific topic
    /// </summary>
    public async Task<bool> PublishAsync(string topic, string payload, bool retainFlag = false)
    {
        try
        {
            if (_mqttClient?.IsConnected != true)
            {
                _logger.LogWarning("MQTT client not connected, cannot publish to {Topic}", topic);
                return false;
            }

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithRetainFlag(retainFlag)
                .Build();

            await _mqttClient.PublishAsync(message);

            _logger.LogDebug("Published message to topic {Topic}", topic);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing message to {Topic}", topic);
            return false;
        }
    }

    public void Dispose()
    {
        _mqttClient?.Dispose();
    }

    private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        try
        {
            var topic = args.ApplicationMessage.Topic;
            var payloadBytes = args.ApplicationMessage.PayloadSegment;

            if (string.IsNullOrWhiteSpace(topic) || payloadBytes.Count == 0)
                return;

            // Handle Zigbee bridge status
            var zigbeePrefix = _mqttOptions.Zigbee2MqttTopicPrefix.Trim().Trim('/');
            if (topic.Equals($"{zigbeePrefix}/bridge/state", StringComparison.OrdinalIgnoreCase))
            {
                var payload = Encoding.UTF8.GetString(payloadBytes);
                if (payload.Contains("online", StringComparison.OrdinalIgnoreCase))
                {
                    _zigbeeBridgeAuthenticatedSinceStartup = true;
                    _logger.LogInformation("Zigbee bridge is online");
                }
                return;
            }

            // Handle device telemetry
            var topicPrefix = _mqttOptions.DeviceTopicPrefix.Trim().Trim('/');
            if (!topic.StartsWith($"{topicPrefix}/", StringComparison.OrdinalIgnoreCase))
                return;

            // Parse topic: devices/MAC/telemetry
            var parts = topic.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3 || !parts[2].Equals("telemetry", StringComparison.OrdinalIgnoreCase))
                return;

            var macAddress = NormalizeMacAddress(parts[1]);
            if (macAddress == null)
            {
                _logger.LogWarning("Invalid MAC address in topic: {Topic}", topic);
                return;
            }

            var payloadString = Encoding.UTF8.GetString(payloadBytes);
            await ProcessSensorDataAsync(macAddress, payloadString);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing MQTT message");
        }
    }

    private async Task OnConnectedAsync(MqttClientConnectedEventArgs args)
    {
        _logger.LogInformation("MQTT client connected");
    }

    private async Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        _isRunning = false;
        _logger.LogWarning("MQTT client disconnected, will attempt to reconnect");
        
        // Attempt to reconnect after delay
        await Task.Delay(TimeSpan.FromSeconds(5));
        try
        {
            await StartAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reconnect to MQTT broker");
        }
    }

    private async Task ProcessSensorDataAsync(string macAddress, string payloadJson)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var sensorService = scope.ServiceProvider.GetRequiredService<ISensorIngestionService>();

            // Find device by MAC address
            var device = await context.Devices
                .FirstOrDefaultAsync(d => d.MacAddress == macAddress);

            if (device == null)
            {
                _logger.LogWarning("Received data from unregistered device: {MacAddress}", macAddress);
                return;
            }

            // Parse sensor data
            using var jsonDoc = JsonDocument.Parse(payloadJson);
            var sensorDto = new SensorDataDto
            {
                MacAddress = macAddress,
                Ph = jsonDoc.RootElement.TryGetProperty("ph", out var ph) ? ph.GetDouble() : null,
                Tds = jsonDoc.RootElement.TryGetProperty("tds", out var tds) ? (int?)tds.GetInt32() : null,
                WaterTemperature = jsonDoc.RootElement.TryGetProperty("water_temp", out var wt) ? wt.GetDouble() : null,
                AirHumidity = jsonDoc.RootElement.TryGetProperty("humidity", out var h) ? h.GetDouble() : null,
                LightIntensity = jsonDoc.RootElement.TryGetProperty("light", out var l) ? (int?)l.GetInt32() : null
            };

            // Ingest sensor data
            await sensorService.ProcessSensorDataAsync(sensorDto);

            _logger.LogDebug("Processed sensor data from device {DeviceId} ({MacAddress})", device.Id, macAddress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing sensor data from {MacAddress}", macAddress);
        }
    }

    private static string? NormalizeMacAddress(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var normalized = System.Text.RegularExpressions.Regex.Replace(input.ToUpper(), "[^0-9A-F]", "");
        return normalized.Length == 12 ? string.Join(":", Enumerable.Range(0, 6).Select(i => normalized.Substring(i * 2, 2))) : null;
    }
}
