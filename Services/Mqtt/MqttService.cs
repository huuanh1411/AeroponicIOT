using AeroponicIOT.Data;
using AeroponicIOT.DTOs;
using AeroponicIOT.Models;
using AeroponicIOT.Options;
using AeroponicIOT.Services.Sensors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Adapter;
using MQTTnet.Diagnostics;
using MQTTnet.Protocol;
using MQTTnet.Server;
using System.Collections.Concurrent;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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
    private readonly MqttSettingsOptions _mqttOptions;
    private readonly ProvisioningOptions _provisioningOptions;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConcurrentDictionary<string, string> _authorizedClientMacs = new(StringComparer.OrdinalIgnoreCase);
    private MqttServer? _mqttServer;
    private bool _isRunning;

    public bool IsRunning => _isRunning;

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
    /// Start the MQTT broker on the configured port
    /// </summary>
    public async Task StartAsync()
    {
        try
        {
            var port = _mqttOptions.Port;
            var optionsBuilder = new MqttServerOptionsBuilder()
                .WithDefaultEndpointPort(port)
                .WithDefaultEndpointBoundIPAddress(System.Net.IPAddress.Any);

            if (_mqttOptions.EnableTls)
            {
                var serverCertificate = LoadServerCertificate(
                    _mqttOptions.ServerCertificatePath,
                    _mqttOptions.ServerCertificatePassword);

                optionsBuilder
                    .WithEncryptedEndpoint()
                    .WithEncryptedEndpointPort(_mqttOptions.TlsPort)
                    .WithEncryptionCertificate(serverCertificate);

                if (_mqttOptions.RequireClientCertificate)
                {
                    optionsBuilder.WithClientCertificate(ValidateClientCertificate, true);
                }

                if (_mqttOptions.DisablePlaintextEndpoint)
                {
                    optionsBuilder.WithoutDefaultEndpoint();
                }
            }

            // If MQTT username/password are configured, enforce connection authentication
            var mqttUser = _mqttOptions.Username;
            var mqttPassword = _mqttOptions.Password;
            var requireClientAuthentication = _mqttOptions.RequireClientAuthentication;

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
                _authorizedClientMacs.TryRemove(e.ClientId, out _);
                await Task.CompletedTask;
            };

            if (requireClientAuthentication)
            {
                _mqttServer.ValidatingConnectionAsync += async e =>
                {
                    if (string.IsNullOrWhiteSpace(e.UserName) || string.IsNullOrWhiteSpace(e.Password))
                    {
                        e.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
                        await Task.CompletedTask;
                        return;
                    }

                    // Administrative credentials are allowed for trusted tools.
                    if (!string.IsNullOrWhiteSpace(mqttUser)
                        && !string.IsNullOrWhiteSpace(mqttPassword)
                        && e.UserName == mqttUser
                        && e.Password == mqttPassword)
                    {
                        e.ReasonCode = MqttConnectReasonCode.Success;
                        await Task.CompletedTask;
                        return;
                    }

                    // Device credentials: username must be MAC and password must match derived secret.
                    var normalizedMac = NormalizeMacAddress(e.UserName);
                    if (normalizedMac == null)
                    {
                        e.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
                        await Task.CompletedTask;
                        return;
                    }

                    var expectedPassword = ComputeDevicePassword(normalizedMac, _provisioningOptions.SharedKey);
                    var providedBytes = Encoding.UTF8.GetBytes(e.Password);
                    var expectedBytes = Encoding.UTF8.GetBytes(expectedPassword);

                    if (!CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes))
                    {
                        e.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
                        await Task.CompletedTask;
                        return;
                    }

                    _authorizedClientMacs[e.ClientId] = normalizedMac;
                    e.ReasonCode = MqttConnectReasonCode.Success;
                    await Task.CompletedTask;
                };
            }

            // Handle incoming published messages (for sensor data ingestion)
            _mqttServer.InterceptingPublishAsync += OnInterceptingPublishAsync;

            await _mqttServer.StartAsync();
            _isRunning = true;

            _logger.LogInformation(
                "MQTT Broker started. PlaintextPort={PlaintextPort}, TlsEnabled={TlsEnabled}, TlsPort={TlsPort}, ClientCertRequired={ClientCertRequired}",
                _mqttOptions.DisablePlaintextEndpoint ? 0 : port,
                _mqttOptions.EnableTls,
                _mqttOptions.EnableTls ? _mqttOptions.TlsPort : 0,
                _mqttOptions.RequireClientCertificate);
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
    public async Task<bool> PublishAsync(string topic, string payload, bool retainFlag = false)
    {
        try
        {
            if (_mqttServer == null || !_isRunning)
            {
                _logger.LogWarning("MQTT Broker not running, cannot publish to {Topic}", topic);
                return false;
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

            // ── Zigbee2MQTT bridge topics ─────────────────────────────────
            if (_mqttOptions.EnableZigbee2MqttBridge)
            {
                var zigbeePrefix = _mqttOptions.Zigbee2MqttTopicPrefix.Trim().Trim('/');
                if (topic.StartsWith($"{zigbeePrefix}/", StringComparison.OrdinalIgnoreCase))
                {
                    var zigbeePayloadBytes = args.ApplicationMessage.PayloadSegment;
                    if (zigbeePayloadBytes.Count > 0)
                    {
                        var zigbeeJson = Encoding.UTF8.GetString(zigbeePayloadBytes);
                        await HandleZigbee2MqttMessageAsync(topic, zigbeePrefix, zigbeeJson);
                    }
                    return;
                }
            }

            // ── Existing WiFi device sensor messages ──────────────────────
            // Expect sensor messages on: devices/{macAddress}/sensor
            var expectedPrefix = _mqttOptions.DeviceTopicPrefix.Trim().Trim('/');
            if (!topic.StartsWith($"{expectedPrefix}/", StringComparison.OrdinalIgnoreCase) ||
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

            if (_mqttOptions.EnforceTopicAcl &&
                !string.Equals(args.ClientId, "ServerPublisher", StringComparison.OrdinalIgnoreCase))
            {
                if (!_authorizedClientMacs.TryGetValue(args.ClientId, out var authorizedMac) ||
                    !string.Equals(authorizedMac, NormalizeMacAddress(macAddress), StringComparison.OrdinalIgnoreCase))
                {
                    args.ProcessPublish = false;
                    _logger.LogWarning(
                        "MQTT publish denied by ACL. ClientId={ClientId}, Topic={Topic}",
                        args.ClientId,
                        topic);
                    return;
                }
            }

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

    // ─────────────────────────────────────────────────────────────────────────
    // Zigbee2MQTT helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Full Zigbee2MQTT message handler that receives the decoded JSON string.
    /// Called from <see cref="OnInterceptingPublishAsync"/> once bytes are read.
    /// </summary>
    private async Task HandleZigbee2MqttMessageAsync(string topic, string zigbeePrefix, string json)
    {
        // ── Bridge lifecycle events ───────────────────────────────────────
        if (topic.Equals($"{zigbeePrefix}/bridge/event", StringComparison.OrdinalIgnoreCase))
        {
            await HandleZigbeeBridgeEventAsync(json);
            return;
        }

        // Ignore other bridge/* management topics (state, log, devices, groups, …)
        if (topic.StartsWith($"{zigbeePrefix}/bridge/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // ── Device telemetry ─────────────────────────────────────────────
        var segments = topic.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            return;
        }

        var friendlyName = segments[1];
        var sensorData = ZigbeePayloadMapper.TryMap(friendlyName, json);
        if (sensorData == null)
        {
            // Frame contains only link-quality / battery / availability – skip.
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var ingestionService = scope.ServiceProvider.GetRequiredService<ISensorIngestionService>();
        await ingestionService.ProcessSensorDataAsync(sensorData, CancellationToken.None);

        _logger.LogInformation(
            "Sensor data ingested via Zigbee2MQTT for device {FriendlyName}", friendlyName);
    }

    /// <summary>
    /// Auto-provisions a new Zigbee device as a <see cref="Device"/> record with
    /// <c>ProtocolType = "zigbee"</c> and <c>Status = Pending</c> when it joins
    /// the coordinator for the first time.  A user can then claim it from the
    /// dashboard using the standard claim flow.
    /// </summary>
    private async Task HandleZigbeeBridgeEventAsync(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeProp))
            {
                return;
            }

            var eventType = typeProp.GetString();

            // Act on first join and on successful interview (full device info available)
            if (eventType is not ("device_joined" or "device_interview_successful"))
            {
                return;
            }

            if (!root.TryGetProperty("data", out var data))
            {
                return;
            }

            var friendlyName  = data.TryGetProperty("friendly_name",  out var fn)   ? fn.GetString()   : null;
            var ieeeAddress   = data.TryGetProperty("ieee_address",   out var ieee) ? ieee.GetString()  : null;

            // IEEE address is the canonical stable identifier; fall back to friendly name.
            var identifier = ieeeAddress ?? friendlyName;
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var existing = await context.Devices
                .FirstOrDefaultAsync(d => d.MacAddress == identifier);

            if (existing != null)
            {
                // Device already known – refresh last-seen timestamp.
                existing.LastSeen = DateTime.UtcNow;
                await context.SaveChangesAsync();
                return;
            }

            var device = new Device
            {
                DeviceName     = friendlyName ?? identifier,
                MacAddress     = identifier,
                ChipId         = ieeeAddress,
                ProtocolType   = "zigbee",
                Status         = DeviceStatusValues.Pending,
                CreatedAt      = DateTime.UtcNow,
                LastSeen       = DateTime.UtcNow,
                ProvisionedAt  = DateTime.UtcNow,
            };

            context.Devices.Add(device);
            await context.SaveChangesAsync();

            _logger.LogInformation(
                "Zigbee device auto-provisioned: {Identifier} (event: {EventType})",
                identifier, eventType);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to handle Zigbee2MQTT bridge event");
        }
    }

    private static string? NormalizeMacAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim().ToUpperInvariant();
        var pattern = @"^([0-9A-F]{2}[:-]){5}([0-9A-F]{2})$";
        if (!System.Text.RegularExpressions.Regex.IsMatch(trimmed, pattern))
        {
            return null;
        }

        return trimmed.Replace('-', ':');
    }

    private static string ComputeDevicePassword(string normalizedMac, string sharedKey)
    {
        var keyBytes = Encoding.UTF8.GetBytes(sharedKey);
        var dataBytes = Encoding.UTF8.GetBytes(normalizedMac);
        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(dataBytes);
        return Convert.ToHexString(hash);
    }

    private static X509Certificate2 LoadServerCertificate(string? certificatePath, string? certificatePassword)
    {
        if (string.IsNullOrWhiteSpace(certificatePath))
        {
            throw new InvalidOperationException("MQTT TLS is enabled but MqttSettings:ServerCertificatePath is not configured.");
        }

        if (!File.Exists(certificatePath))
        {
            throw new FileNotFoundException("MQTT server certificate file was not found.", certificatePath);
        }

        return new X509Certificate2(certificatePath, certificatePassword);
    }

    private bool ValidateClientCertificate(
        object sender,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors)
    {
        if (certificate is null || sslPolicyErrors != SslPolicyErrors.None)
        {
            return false;
        }

        var cert2 = certificate as X509Certificate2 ?? new X509Certificate2(certificate);

        var allowlistedIssuers = _mqttOptions.AllowedClientCertificateIssuers
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .ToArray();

        var allowlistedThumbprints = _mqttOptions.AllowedClientCertificateThumbprints
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(NormalizeThumbprint)
            .ToArray();

        var issuerMatched = allowlistedIssuers.Length == 0 ||
            allowlistedIssuers.Any(issuer =>
                string.Equals(cert2.Issuer, issuer, StringComparison.OrdinalIgnoreCase));

        var certThumbprint = NormalizeThumbprint(cert2.Thumbprint ?? string.Empty);
        var thumbprintMatched = allowlistedThumbprints.Length == 0 ||
            allowlistedThumbprints.Contains(certThumbprint, StringComparer.OrdinalIgnoreCase);

        return issuerMatched && thumbprintMatched;
    }

    private static string NormalizeThumbprint(string thumbprint)
    {
        return thumbprint.Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();
    }
}
