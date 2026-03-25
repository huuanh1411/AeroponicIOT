using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AeroponicIOT.Data;
using AeroponicIOT.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AeroponicIOT.Tests;

public class SensorErrorMappingIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public SensorErrorMappingIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ReceiveSensorData_MissingMacAddress_ReturnsBadRequestProblem()
    {
        await ResetDatabaseAsync();

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Device-Key", "test-shared-key-123");

        var response = await client.PostAsJsonAsync("/api/sensor", new
        {
            ph = 6.3,
            tds = 900
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Bad Request", payload.GetProperty("title").GetString());
        Assert.Contains("MAC address", payload.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReceiveSensorData_UnknownMacAddress_ReturnsNotFoundProblem()
    {
        await ResetDatabaseAsync();

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Device-Key", "test-shared-key-123");

        var response = await client.PostAsJsonAsync("/api/sensor", new
        {
            macAddress = "AA:BB:CC:DD:EE:99",
            ph = 6.2,
            tds = 980
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Not Found", payload.GetProperty("title").GetString());
        Assert.Contains("not found", payload.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    private async Task ResetDatabaseAsync()
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
    }
}
