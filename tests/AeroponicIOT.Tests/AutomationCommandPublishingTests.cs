using System.Reflection;
using AeroponicIOT.Data;
using AeroponicIOT.Models;
using AeroponicIOT.Services.Automation;
using AeroponicIOT.Services.Mqtt;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AeroponicIOT.Tests;

public class AutomationCommandPublishingTests
{
    [Fact]
    public async Task ExecuteRuleAsyncPublishesNonRetainedControlMessage()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"automation-publish-{Guid.NewGuid()}")
            .Options;

        await using var dbContext = new ApplicationDbContext(options);

        var device = new Device
        {
            Id = 10,
            DeviceName = "Unit-10",
            MacAddress = "AA:BB:CC:DD:EE:10",
            CreatedAt = DateTime.UtcNow,
            Status = DeviceStatusValues.Active
        };

        dbContext.Devices.Add(device);
        await dbContext.SaveChangesAsync();

        var rule = new AutomationRule
        {
            Id = 5,
            DeviceId = device.Id,
            RuleName = "Pump schedule",
            RuleType = 0,
            ActuatorType = 0,
            Action = "ON",
            IsActive = true,
            Priority = 1,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.AutomationRules.Add(rule);
        await dbContext.SaveChangesAsync();
        rule.Device = device;

        var mqtt = new CapturingMqttService();

        var method = typeof(AutomationBackgroundService).GetMethod(
            "ExecuteRuleAsync",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var task = (Task?)method!.Invoke(null, new object[]
        {
            rule,
            dbContext,
            mqtt,
            DateTime.UtcNow,
            CancellationToken.None
        });

        Assert.NotNull(task);
        await task!;
        await dbContext.SaveChangesAsync();

        Assert.False(mqtt.LastRetainFlag);
        Assert.Equal($"devices/{device.MacAddress}/control", mqtt.LastTopic);
        Assert.Single(dbContext.ActuatorLogs);
        Assert.True(rule.LastExecuted.HasValue);
    }

    private sealed class CapturingMqttService : IMqttService
    {
        public string LastTopic { get; private set; } = string.Empty;
        public bool LastRetainFlag { get; private set; }

        public bool IsRunning => true;
        public bool IsZigbeeBridgeReady => true;
        public string ZigbeeBridgeReadinessMessage => "ready";

        public Task StartAsync() => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;

        public Task<bool> PublishAsync(string topic, string payload, bool retainFlag = false)
        {
            LastTopic = topic;
            LastRetainFlag = retainFlag;
            return Task.FromResult(true);
        }
    }
}
