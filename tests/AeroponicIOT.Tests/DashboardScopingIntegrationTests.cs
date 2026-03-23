using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AeroponicIOT.Data;
using AeroponicIOT.Models;
using AeroponicIOT.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AeroponicIOT.Tests;

public class DashboardScopingIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public DashboardScopingIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Latest_Farmer_SeesOnlyOwnUnresolvedAlerts()
    {
        using var client = _factory.CreateClient();
        await SeedDashboardDataAsync();

        client.DefaultRequestHeaders.Add("X-Test-UserId", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Farmer");

        var response = await client.GetAsync("/api/dashboard/latest");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var activeAlerts = doc.RootElement.GetProperty("data").GetProperty("activeAlerts");

        Assert.Equal(1, activeAlerts.GetArrayLength());
        Assert.Equal(1, activeAlerts[0].GetProperty("deviceId").GetInt32());
    }

    [Fact]
    public async Task Kpi_Farmer_SeesOnlyOwnActiveAlertCount()
    {
        using var client = _factory.CreateClient();
        await SeedDashboardDataAsync();

        client.DefaultRequestHeaders.Add("X-Test-UserId", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Farmer");

        var response = await client.GetAsync("/api/dashboard/kpi");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, payload.GetProperty("data").GetProperty("activeAlerts").GetInt32());
    }

    [Fact]
    public async Task Latest_Admin_SeesAllUnresolvedAlerts()
    {
        using var client = _factory.CreateClient();
        await SeedDashboardDataAsync();

        client.DefaultRequestHeaders.Add("X-Test-UserId", "99");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Administrator");

        var response = await client.GetAsync("/api/dashboard/latest");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var activeAlerts = doc.RootElement.GetProperty("data").GetProperty("activeAlerts");

        Assert.Equal(2, activeAlerts.GetArrayLength());
    }

    private async Task SeedDashboardDataAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        db.Users.RemoveRange(db.Users);
        db.Alerts.RemoveRange(db.Alerts);
        db.Devices.RemoveRange(db.Devices);
        await db.SaveChangesAsync();

        db.Users.AddRange(
            new User
            {
                Id = 1,
                Username = "farmer-1",
                Email = "farmer1@test.local",
                PasswordHash = "hash",
                Role = "Farmer",
                CreatedAt = DateTime.UtcNow
            },
            new User
            {
                Id = 2,
                Username = "farmer-2",
                Email = "farmer2@test.local",
                PasswordHash = "hash",
                Role = "Farmer",
                CreatedAt = DateTime.UtcNow
            },
            new User
            {
                Id = 99,
                Username = "admin",
                Email = "admin@test.local",
                PasswordHash = "hash",
                Role = "Administrator",
                CreatedAt = DateTime.UtcNow
            });

        db.Devices.AddRange(
            new Device
            {
                Id = 1,
                DeviceName = "Device-1",
                MacAddress = "AA:BB:CC:DD:EE:01",
                UserId = 1,
                Status = "Active",
                CreatedAt = DateTime.UtcNow,
                LastSeen = DateTime.UtcNow
            },
            new Device
            {
                Id = 2,
                DeviceName = "Device-2",
                MacAddress = "AA:BB:CC:DD:EE:02",
                UserId = 2,
                Status = "Active",
                CreatedAt = DateTime.UtcNow,
                LastSeen = DateTime.UtcNow
            });

        db.Alerts.AddRange(
            new Alert
            {
                DeviceId = 1,
                AlertType = "Warning",
                Message = "Farmer 1 alert",
                Severity = "High",
                IsResolved = false,
                Timestamp = DateTime.UtcNow
            },
            new Alert
            {
                DeviceId = 2,
                AlertType = "Warning",
                Message = "Farmer 2 alert",
                Severity = "High",
                IsResolved = false,
                Timestamp = DateTime.UtcNow
            },
            new Alert
            {
                DeviceId = 1,
                AlertType = "Info",
                Message = "Resolved alert should be ignored",
                Severity = "Low",
                IsResolved = true,
                Timestamp = DateTime.UtcNow
            });

        await db.SaveChangesAsync();
    }
}
