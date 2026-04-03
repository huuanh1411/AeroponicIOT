using System.Text.Json;
using AeroponicIOT.Data;
using AeroponicIOT.Models;
using AeroponicIOT.Services.Mqtt;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace AeroponicIOT.Services.Automation;

/// <summary>
/// Background service that executes automation rules (schedule- and threshold-based)
/// and sends actuator commands via MQTT.
/// </summary>
public class AutomationBackgroundService : BackgroundService
{
    private readonly ILogger<AutomationBackgroundService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public AutomationBackgroundService(
        ILogger<AutomationBackgroundService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small initial delay so the app and MQTT broker can finish starting.
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAutomationCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Normal on shutdown.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during automation cycle.");
            }

            // Run roughly once per minute.
            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
    }

    private async Task RunAutomationCycleAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var mqttService = scope.ServiceProvider.GetRequiredService<IMqttService>();

        var nowUtc = DateTime.UtcNow;

        var rules = await dbContext.AutomationRules
            .Include(r => r.Device)
            .Where(r => r.IsActive)
            .OrderByDescending(r => r.Priority)
            .ToListAsync(cancellationToken);

        foreach (var rule in rules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (rule.Device == null || string.IsNullOrWhiteSpace(rule.Device.MacAddress))
            {
                continue;
            }

            var shouldExecute = rule.RuleType switch
            {
                0 => ShouldExecuteScheduleRule(rule, nowUtc),
                1 => await ShouldExecuteThresholdRuleAsync(rule, dbContext, nowUtc, cancellationToken),
                2 => ShouldExecuteTimerRule(rule, nowUtc),
                _ => false
            };

            if (!shouldExecute)
            {
                continue;
            }

            try
            {
                await ExecuteRuleAsync(rule, dbContext, mqttService, nowUtc, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing automation rule {RuleId}", rule.Id);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static bool ShouldExecuteScheduleRule(AutomationRule rule, DateTime nowUtc)
    {
        if (rule.ScheduleTime is null)
        {
            return false;
        }

        // Interpret schedule time in UTC for simplicity.
        var now = nowUtc;
        var todayName = now.DayOfWeek.ToString();

        if (!string.IsNullOrWhiteSpace(rule.ScheduleDays))
        {
            var days = rule.ScheduleDays
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (!days.Any(d => string.Equals(d, todayName, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        var ruleTime = rule.ScheduleTime.Value.ToTimeSpan();
        var diff = (now.TimeOfDay - ruleTime).Duration();

        // Only trigger close to the configured minute.
        if (diff > TimeSpan.FromMinutes(1))
        {
            return false;
        }

        // Avoid re-executing within the same minute.
        if (rule.LastExecuted.HasValue &&
            (nowUtc - rule.LastExecuted.Value) < TimeSpan.FromMinutes(1))
        {
            return false;
        }

        return true;
    }

    private static async Task<bool> ShouldExecuteThresholdRuleAsync(
        AutomationRule rule,
        ApplicationDbContext dbContext,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rule.ConditionParameter) ||
            string.IsNullOrWhiteSpace(rule.ConditionOperator) ||
            rule.ConditionValue is null)
        {
            return false;
        }

        // Simple cooldown so we don't spam commands.
        if (rule.LastExecuted.HasValue &&
            (nowUtc - rule.LastExecuted.Value) < TimeSpan.FromMinutes(5))
        {
            return false;
        }

        var latestLog = await dbContext.SensorLogs
            .Where(sl => sl.DeviceId == rule.DeviceId)
            .OrderByDescending(sl => sl.Timestamp)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestLog == null)
        {
            return false;
        }

        decimal? parameterValue = rule.ConditionParameter.ToLower(CultureInfo.InvariantCulture) switch
        {
            "ph" => latestLog.Ph,
            "tds" or "tds_ppm" or "ec" => latestLog.TdsPpm,
            "temperature" or "water_temp" => latestLog.WaterTemp,
            "humidity" or "air_humidity" => latestLog.Humidity,
            _ => null
        };

        if (parameterValue is null)
        {
            return false;
        }

        return EvaluateCondition(parameterValue.Value, rule.ConditionValue.Value, rule.ConditionOperator);
    }

    private static bool EvaluateCondition(decimal actual, decimal expected, string? op)
    {
        return op switch
        {
            ">" => actual > expected,
            "<" => actual < expected,
            "==" => actual == expected,
            ">=" => actual >= expected,
            "<=" => actual <= expected,
            _ => false
        };
    }

    /// <summary>
    /// Timer (fixed-duration / pulse-style) rules.
    /// These are treated as "fire every DurationMinutes" with a minimum 1-minute spacing,
    /// regardless of sensor values.
    /// </summary>
    private static bool ShouldExecuteTimerRule(AutomationRule rule, DateTime nowUtc)
    {
        // If no duration is provided, treat as simple periodic trigger every 5 minutes.
        var interval = rule.DurationMinutes.HasValue && rule.DurationMinutes.Value > 0
            ? TimeSpan.FromMinutes(rule.DurationMinutes.Value)
            : TimeSpan.FromMinutes(5);

        if (rule.LastExecuted is null)
        {
            // First execution happens as soon as the service checks rules.
            return true;
        }

        var sinceLast = nowUtc - rule.LastExecuted.Value;

        // Fire when we've passed the configured interval, with a small minimum spacing.
        return sinceLast >= interval && sinceLast >= TimeSpan.FromMinutes(1);
    }

    private static async Task ExecuteRuleAsync(
        AutomationRule rule,
        ApplicationDbContext dbContext,
        IMqttService mqttService,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var device = rule.Device;
        if (device == null || string.IsNullOrWhiteSpace(device.MacAddress))
        {
            return;
        }

        var action = string.IsNullOrWhiteSpace(rule.Action)
            ? "ON"
            : rule.Action.ToUpperInvariant();

        // Record actuator log.
        var actuatorLog = new ActuatorLog
        {
            DeviceId = device.Id,
            Timestamp = nowUtc,
            ActuatorType = GetActuatorTypeName(rule.ActuatorType),
            Action = action,
            DurationMinutes = rule.DurationMinutes
        };

        dbContext.ActuatorLogs.Add(actuatorLog);

        // Publish MQTT control command.
        var topic = $"devices/{device.MacAddress}/control";

        var payload = new
        {
            deviceId = device.Id,
            deviceName = device.Name,
            macAddress = device.MacAddress,
            actuatorType = rule.ActuatorType,
            action,
            timestamp = nowUtc
        };

        var jsonPayload = JsonSerializer.Serialize(payload);

        // Control commands should not be retained to avoid replaying stale actions on reconnect.
        var delivered = await mqttService.PublishAsync(topic, jsonPayload, retainFlag: false);
        if (!delivered)
        {
            throw new InvalidOperationException($"MQTT publish failed for automation rule {rule.Id}");
        }

        // Update rule execution time to enforce cooldowns.
        rule.LastExecuted = nowUtc;
        dbContext.AutomationRules.Update(rule);
    }

    private static string GetActuatorTypeName(int actuatorType) =>
        actuatorType switch
        {
            0 => "Pump",
            1 => "Fan",
            2 => "Light",
            3 => "Heater",
            _ => "Pump"
        };
}

