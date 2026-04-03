using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AeroponicIOT.Data;
using AeroponicIOT.Models;
using AeroponicIOT.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AeroponicIOT.Tests;

public class GardenAuthorizationIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public GardenAuthorizationIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetGardenDevicesScopesResultsForFarmer()
    {
        await ResetDatabaseAsync(db =>
        {
            db.Users.AddRange(
                new User { Id = 1, Username = "farmer-1", Email = "farmer1@test.local", PasswordHash = "hash", Role = "Farmer", CreatedAt = DateTime.UtcNow },
                new User { Id = 2, Username = "farmer-2", Email = "farmer2@test.local", PasswordHash = "hash", Role = "Farmer", CreatedAt = DateTime.UtcNow },
                new User { Id = 99, Username = "admin", Email = "admin@test.local", PasswordHash = "hash", Role = "Administrator", CreatedAt = DateTime.UtcNow });

            db.Gardens.Add(new Garden { Id = 1, Name = "Main Garden", CreatedAt = DateTime.UtcNow });

            db.Devices.AddRange(
                new Device { Id = 1, DeviceName = "Owned-1", MacAddress = "AA:BB:CC:DD:EE:21", UserId = 1, GardenId = 1, Status = "Active", CreatedAt = DateTime.UtcNow, LastSeen = DateTime.UtcNow },
                new Device { Id = 2, DeviceName = "Owned-2", MacAddress = "AA:BB:CC:DD:EE:22", UserId = 2, GardenId = 1, Status = "Active", CreatedAt = DateTime.UtcNow, LastSeen = DateTime.UtcNow });
        });

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-UserId", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Farmer");

        var response = await client.GetAsync("/api/garden/1/devices");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var data = payload.GetProperty("data");
        Assert.Equal(1, data.GetArrayLength());
        Assert.Equal(1, data[0].GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task GetGardenDevicesReturnsAllForAdmin()
    {
        await ResetDatabaseAsync(db =>
        {
            db.Users.Add(new User { Id = 99, Username = "admin", Email = "admin@test.local", PasswordHash = "hash", Role = "Administrator", CreatedAt = DateTime.UtcNow });
            db.Gardens.Add(new Garden { Id = 1, Name = "Main Garden", CreatedAt = DateTime.UtcNow });
            db.Devices.AddRange(
                new Device { Id = 1, DeviceName = "Owned-1", MacAddress = "AA:BB:CC:DD:EE:21", UserId = 1, GardenId = 1, Status = "Active", CreatedAt = DateTime.UtcNow, LastSeen = DateTime.UtcNow },
                new Device { Id = 2, DeviceName = "Owned-2", MacAddress = "AA:BB:CC:DD:EE:22", UserId = 2, GardenId = 1, Status = "Active", CreatedAt = DateTime.UtcNow, LastSeen = DateTime.UtcNow });
        });

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-UserId", "99");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Administrator");

        var response = await client.GetAsync("/api/garden/1/devices");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, payload.GetProperty("data").GetArrayLength());
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
