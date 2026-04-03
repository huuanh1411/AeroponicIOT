using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AeroponicIOT.Data;
using AeroponicIOT.Models;
using AeroponicIOT.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AeroponicIOT.Tests;

public class ActuatorAuthorizationIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ActuatorAuthorizationIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ControlActuatorReturnsForbiddenForNonOwnerFarmer()
    {
        await ResetDatabaseAsync(db =>
        {
            db.Users.AddRange(
                new User { Id = 1, Username = "owner", Email = "owner@test.local", PasswordHash = "hash", Role = "Farmer", CreatedAt = DateTime.UtcNow },
                new User { Id = 2, Username = "other", Email = "other@test.local", PasswordHash = "hash", Role = "Farmer", CreatedAt = DateTime.UtcNow });

            db.Devices.Add(new Device
            {
                Id = 10,
                DeviceName = "Pump Device",
                MacAddress = "AA:BB:CC:DD:EE:31",
                UserId = 1,
                Status = DeviceStatusValues.Active,
                CreatedAt = DateTime.UtcNow,
                LastSeen = DateTime.UtcNow
            });
        });

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-UserId", "2");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Farmer");

        var response = await client.PostAsJsonAsync("/api/actuator/control", new
        {
            macAddress = "AA:BB:CC:DD:EE:31",
            actuatorType = "Pump",
            action = "ON"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetActuatorLogsReturnsDtoContractForAuthorizedUsers()
    {
        await ResetDatabaseAsync(db =>
        {
            db.Users.AddRange(
                new User { Id = 1, Username = "owner", Email = "owner@test.local", PasswordHash = "hash", Role = "Farmer", CreatedAt = DateTime.UtcNow },
                new User { Id = 99, Username = "admin", Email = "admin@test.local", PasswordHash = "hash", Role = "Administrator", CreatedAt = DateTime.UtcNow });

            db.Devices.Add(new Device
            {
                Id = 10,
                DeviceName = "Pump Device",
                MacAddress = "AA:BB:CC:DD:EE:31",
                UserId = 1,
                Status = DeviceStatusValues.Active,
                CreatedAt = DateTime.UtcNow,
                LastSeen = DateTime.UtcNow
            });

            db.ActuatorLogs.Add(new ActuatorLog
            {
                Id = 50,
                DeviceId = 10,
                ActuatorType = "Pump",
                Action = "ON",
                Timestamp = DateTime.UtcNow
            });
        });

        using var ownerClient = _factory.CreateClient();
        ownerClient.DefaultRequestHeaders.Add("X-Test-UserId", "1");
        ownerClient.DefaultRequestHeaders.Add("X-Test-Role", "Farmer");

        var ownerResponse = await ownerClient.GetAsync("/api/actuator/logs/10");
        Assert.Equal(HttpStatusCode.OK, ownerResponse.StatusCode);
        var ownerPayload = await ownerResponse.Content.ReadFromJsonAsync<JsonElement>();
        var logEntries = ownerPayload.GetProperty("data");
        Assert.Single(logEntries.EnumerateArray());

        var firstLog = logEntries[0];
        Assert.Equal(50, firstLog.GetProperty("id").GetInt32());
        Assert.Equal(10, firstLog.GetProperty("deviceId").GetInt32());
        Assert.Equal("Pump Device", firstLog.GetProperty("deviceName").GetString());
        Assert.Equal("AA:BB:CC:DD:EE:31", firstLog.GetProperty("macAddress").GetString());
        Assert.Equal("Pump", firstLog.GetProperty("actuatorType").GetString());
        Assert.Equal("ON", firstLog.GetProperty("action").GetString());
        Assert.True(firstLog.TryGetProperty("timestamp", out _));

        using var adminClient = _factory.CreateClient();
        adminClient.DefaultRequestHeaders.Add("X-Test-UserId", "99");
        adminClient.DefaultRequestHeaders.Add("X-Test-Role", "Administrator");

        var adminResponse = await adminClient.GetAsync("/api/actuator/logs/10");
        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
        var adminPayload = await adminResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Single(adminPayload.GetProperty("data").EnumerateArray());
    }

    private async Task ResetDatabaseAsync(Action<ApplicationDbContext> seed)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        db.SensorLogs.RemoveRange(db.SensorLogs);
        db.ActuatorLogs.RemoveRange(db.ActuatorLogs);
        db.Alerts.RemoveRange(db.Alerts);
        db.AutomationRules.RemoveRange(db.AutomationRules);
        db.Notifications.RemoveRange(db.Notifications);
        db.Devices.RemoveRange(db.Devices);
        db.CropStages.RemoveRange(db.CropStages);
        db.Crops.RemoveRange(db.Crops);
        db.Gardens.RemoveRange(db.Gardens);
        db.Users.RemoveRange(db.Users);

        await db.SaveChangesAsync();

        seed(db);
        await db.SaveChangesAsync();
    }
}