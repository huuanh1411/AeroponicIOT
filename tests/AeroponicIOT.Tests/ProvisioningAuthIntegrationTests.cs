using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AeroponicIOT.Data;
using AeroponicIOT.Models;
using AeroponicIOT.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AeroponicIOT.Tests;

public class ProvisioningAuthIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ProvisioningAuthIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AuthenticationMe_WithoutHeaders_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/authentication/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ClaimDevice_WithExpiredCode_ReturnsBadRequest()
    {
        await ResetDatabaseAsync(db =>
        {
            db.Users.Add(new User
            {
                Id = 1,
                Username = "farmer-1",
                Email = "farmer1@test.local",
                PasswordHash = "hash",
                Role = "Farmer",
                CreatedAt = DateTime.UtcNow
            });

            db.Devices.Add(new Device
            {
                DeviceName = "Pending Device",
                MacAddress = "AA:BB:CC:DD:EE:10",
                Status = "Pending",
                CreatedAt = DateTime.UtcNow,
                LastSeen = DateTime.UtcNow,
                ClaimCode = "ZXCVBN",
                ClaimCodeExpiresAt = DateTime.UtcNow.AddMinutes(-5)
            });
        });

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-UserId", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Farmer");

        var response = await client.PostAsJsonAsync("/api/device/claim", new
        {
            claimCode = "ZXCVBN"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Bad Request", payload.GetProperty("title").GetString());
        Assert.Contains("expired", payload.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ClaimDevice_ClaimCodeCannotBeReused()
    {
        await ResetDatabaseAsync(db =>
        {
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
                });
        });

        using var registrationClient = _factory.CreateClient();
        registrationClient.DefaultRequestHeaders.Add("X-Device-Key", "test-shared-key-123");

        var registerResponse = await registrationClient.PostAsJsonAsync("/api/device/self-register", new
        {
            macAddress = "AA:BB:CC:DD:EE:11",
            deviceName = "Provisioned Device"
        });

        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        var registerPayload = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();
        var claimCode = registerPayload.GetProperty("data").GetProperty("claimCode").GetString();
        Assert.False(string.IsNullOrWhiteSpace(claimCode));

        using var firstClaimClient = _factory.CreateClient();
        firstClaimClient.DefaultRequestHeaders.Add("X-Test-UserId", "1");
        firstClaimClient.DefaultRequestHeaders.Add("X-Test-Role", "Farmer");

        var firstClaimResponse = await firstClaimClient.PostAsJsonAsync("/api/device/claim", new
        {
            claimCode
        });

        Assert.Equal(HttpStatusCode.OK, firstClaimResponse.StatusCode);

        using var secondClaimClient = _factory.CreateClient();
        secondClaimClient.DefaultRequestHeaders.Add("X-Test-UserId", "2");
        secondClaimClient.DefaultRequestHeaders.Add("X-Test-Role", "Farmer");

        var secondClaimResponse = await secondClaimClient.PostAsJsonAsync("/api/device/claim", new
        {
            claimCode
        });

        Assert.Equal(HttpStatusCode.NotFound, secondClaimResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var claimedDevice = await db.Devices.SingleAsync(d => d.MacAddress == "AA:BB:CC:DD:EE:11");

        Assert.Equal(1, claimedDevice.UserId);
        Assert.Null(claimedDevice.ClaimCode);
        Assert.Null(claimedDevice.ClaimCodeExpiresAt);
    }

    [Fact]
    public async Task PendingDevices_FarmerForbidden_AdminAllowed()
    {
        await ResetDatabaseAsync(db =>
        {
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
                    Id = 99,
                    Username = "admin",
                    Email = "admin@test.local",
                    PasswordHash = "hash",
                    Role = "Administrator",
                    CreatedAt = DateTime.UtcNow
                });

            db.Devices.Add(new Device
            {
                Id = 1,
                DeviceName = "Pending Device",
                MacAddress = "AA:BB:CC:DD:EE:12",
                Status = "Pending",
                CreatedAt = DateTime.UtcNow,
                LastSeen = DateTime.UtcNow,
                ProvisionedAt = DateTime.UtcNow
            });
        });

        using var farmerClient = _factory.CreateClient();
        farmerClient.DefaultRequestHeaders.Add("X-Test-UserId", "1");
        farmerClient.DefaultRequestHeaders.Add("X-Test-Role", "Farmer");

        var farmerResponse = await farmerClient.GetAsync("/api/device/pending");
        Assert.Equal(HttpStatusCode.Forbidden, farmerResponse.StatusCode);

        using var adminClient = _factory.CreateClient();
        adminClient.DefaultRequestHeaders.Add("X-Test-UserId", "99");
        adminClient.DefaultRequestHeaders.Add("X-Test-Role", "Administrator");

        var adminResponse = await adminClient.GetAsync("/api/device/pending");
        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);

        var payload = await adminResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, payload.GetProperty("data").GetArrayLength());
    }

    [Fact]
    public async Task GetDeviceById_FarmerCannotAccessOtherUsersDevice_ButAdminCan()
    {
        await ResetDatabaseAsync(db =>
        {
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

            db.Devices.Add(new Device
            {
                Id = 2,
                DeviceName = "Owned by Farmer 2",
                MacAddress = "AA:BB:CC:DD:EE:13",
                UserId = 2,
                Status = "Active",
                CreatedAt = DateTime.UtcNow,
                LastSeen = DateTime.UtcNow
            });
        });

        using var farmerClient = _factory.CreateClient();
        farmerClient.DefaultRequestHeaders.Add("X-Test-UserId", "1");
        farmerClient.DefaultRequestHeaders.Add("X-Test-Role", "Farmer");

        var farmerResponse = await farmerClient.GetAsync("/api/device/2");
        Assert.Equal(HttpStatusCode.Forbidden, farmerResponse.StatusCode);

        using var adminClient = _factory.CreateClient();
        adminClient.DefaultRequestHeaders.Add("X-Test-UserId", "99");
        adminClient.DefaultRequestHeaders.Add("X-Test-Role", "Administrator");

        var adminResponse = await adminClient.GetAsync("/api/device/2");
        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
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
