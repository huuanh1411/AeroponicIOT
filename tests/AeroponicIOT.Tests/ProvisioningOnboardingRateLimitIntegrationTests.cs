using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Reflection;
using AeroponicIOT.Controllers;
using AeroponicIOT.Data;
using AeroponicIOT.Models;
using AeroponicIOT.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AeroponicIOT.Tests;

public class ProvisioningOnboardingRateLimitIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ProvisioningOnboardingRateLimitIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SelfRegister_InvalidProvisioningKey_TriggersCooldownAndRetryAfter()
    {
        await ResetDatabaseAsync(_ => { });
        ClearOnboardingAttemptState();

        using var client = _factory.CreateClient();
        var payload = new
        {
            macAddress = "AA:BB:CC:DD:EE:41",
            deviceName = "Rate Limited Device"
        };

        for (var i = 0; i < 5; i++)
        {
            client.DefaultRequestHeaders.Remove("X-Device-Key");
            client.DefaultRequestHeaders.Add("X-Device-Key", "wrong-key");

            var invalidResponse = await client.PostAsJsonAsync("/api/device/self-register", payload);
            Assert.Equal(HttpStatusCode.Unauthorized, invalidResponse.StatusCode);
        }

        client.DefaultRequestHeaders.Remove("X-Device-Key");
        client.DefaultRequestHeaders.Add("X-Device-Key", "wrong-key");

        var blockedResponse = await client.PostAsJsonAsync("/api/device/self-register", payload);

        Assert.Equal(HttpStatusCode.TooManyRequests, blockedResponse.StatusCode);
        Assert.True(blockedResponse.Headers.TryGetValues("Retry-After", out var retryAfterValues));
        Assert.True(int.TryParse(retryAfterValues.Single(), out var retryAfterSeconds));
        Assert.True(retryAfterSeconds > 0);

        var responsePayload = await blockedResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Too Many Requests", responsePayload.GetProperty("title").GetString());
    }

    [Fact]
    public async Task ClaimDevice_InvalidClaimCode_TriggersCooldownAndRetryAfter()
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
        });
        ClearOnboardingAttemptState();

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-UserId", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Farmer");

        for (var i = 0; i < 5; i++)
        {
            var invalidResponse = await client.PostAsJsonAsync("/api/device/claim", new
            {
                claimCode = "QQQQQQ"
            });

            Assert.Equal(HttpStatusCode.NotFound, invalidResponse.StatusCode);
        }

        var blockedResponse = await client.PostAsJsonAsync("/api/device/claim", new
        {
            claimCode = "QQQQQQ"
        });

        Assert.Equal(HttpStatusCode.TooManyRequests, blockedResponse.StatusCode);
        Assert.True(blockedResponse.Headers.TryGetValues("Retry-After", out var retryAfterValues));
        Assert.True(int.TryParse(retryAfterValues.Single(), out var retryAfterSeconds));
        Assert.True(retryAfterSeconds > 0);

        var responsePayload = await blockedResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Too Many Requests", responsePayload.GetProperty("title").GetString());
    }

    [Fact]
    public async Task SelfRegister_CooldownIsScopedByMacAddress_NotGlobal()
    {
        await ResetDatabaseAsync(_ => { });
        ClearOnboardingAttemptState();

        using var client = _factory.CreateClient();

        var throttledPayload = new
        {
            macAddress = "AA:BB:CC:DD:EE:51",
            deviceName = "Throttled Device"
        };

        for (var i = 0; i < 5; i++)
        {
            client.DefaultRequestHeaders.Remove("X-Device-Key");
            client.DefaultRequestHeaders.Add("X-Device-Key", "wrong-key");

            var invalidResponse = await client.PostAsJsonAsync("/api/device/self-register", throttledPayload);
            Assert.Equal(HttpStatusCode.Unauthorized, invalidResponse.StatusCode);
        }

        client.DefaultRequestHeaders.Remove("X-Device-Key");
        client.DefaultRequestHeaders.Add("X-Device-Key", "wrong-key");
        var blockedResponse = await client.PostAsJsonAsync("/api/device/self-register", throttledPayload);
        Assert.Equal(HttpStatusCode.TooManyRequests, blockedResponse.StatusCode);

        var independentPayload = new
        {
            macAddress = "AA:BB:CC:DD:EE:52",
            deviceName = "Independent Device"
        };

        client.DefaultRequestHeaders.Remove("X-Device-Key");
        client.DefaultRequestHeaders.Add("X-Device-Key", "test-shared-key-123");

        var independentResponse = await client.PostAsJsonAsync("/api/device/self-register", independentPayload);
        Assert.Equal(HttpStatusCode.OK, independentResponse.StatusCode);

        var payload = await independentResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(payload.GetProperty("data").GetProperty("deviceId").GetInt32() > 0);
        Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("data").GetProperty("claimCode").GetString()));
    }

    [Fact]
    public async Task SelfRegister_CooldownExpiry_AllowsRequestAfterStateExpires()
    {
        await ResetDatabaseAsync(_ => { });
        ClearOnboardingAttemptState();

        using var client = _factory.CreateClient();
        var payload = new
        {
            macAddress = "AA:BB:CC:DD:EE:61",
            deviceName = "Cooldown Expiry Device"
        };

        for (var i = 0; i < 5; i++)
        {
            client.DefaultRequestHeaders.Remove("X-Device-Key");
            client.DefaultRequestHeaders.Add("X-Device-Key", "wrong-key");

            var invalidResponse = await client.PostAsJsonAsync("/api/device/self-register", payload);
            Assert.Equal(HttpStatusCode.Unauthorized, invalidResponse.StatusCode);
        }

        var blockedResponse = await client.PostAsJsonAsync("/api/device/self-register", payload);
        Assert.Equal(HttpStatusCode.TooManyRequests, blockedResponse.StatusCode);

        ForceExpireOnboardingCooldowns();

        client.DefaultRequestHeaders.Remove("X-Device-Key");
        client.DefaultRequestHeaders.Add("X-Device-Key", "test-shared-key-123");

        var allowedResponse = await client.PostAsJsonAsync("/api/device/self-register", payload);
        Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);
    }

    [Fact]
    public async Task SelfRegister_SuccessResetsFailedAttemptCounterForAttemptKey()
    {
        await ResetDatabaseAsync(_ => { });
        ClearOnboardingAttemptState();

        using var client = _factory.CreateClient();
        var payload = new
        {
            macAddress = "AA:BB:CC:DD:EE:71",
            deviceName = "Reset Counter Device"
        };

        for (var i = 0; i < 3; i++)
        {
            client.DefaultRequestHeaders.Remove("X-Device-Key");
            client.DefaultRequestHeaders.Add("X-Device-Key", "wrong-key");

            var invalidResponse = await client.PostAsJsonAsync("/api/device/self-register", payload);
            Assert.Equal(HttpStatusCode.Unauthorized, invalidResponse.StatusCode);
        }

        client.DefaultRequestHeaders.Remove("X-Device-Key");
        client.DefaultRequestHeaders.Add("X-Device-Key", "test-shared-key-123");

        var successResponse = await client.PostAsJsonAsync("/api/device/self-register", payload);
        Assert.Equal(HttpStatusCode.OK, successResponse.StatusCode);

        client.DefaultRequestHeaders.Remove("X-Device-Key");
        client.DefaultRequestHeaders.Add("X-Device-Key", "wrong-key");

        for (var i = 0; i < 5; i++)
        {
            var invalidResponse = await client.PostAsJsonAsync("/api/device/self-register", payload);
            Assert.Equal(HttpStatusCode.Unauthorized, invalidResponse.StatusCode);
        }

        var blockedResponse = await client.PostAsJsonAsync("/api/device/self-register", payload);
        Assert.Equal(HttpStatusCode.TooManyRequests, blockedResponse.StatusCode);
    }

    private static void ClearOnboardingAttemptState()
    {
        var dictionary = GetOnboardingAttemptDictionary();
        var clearMethod = dictionary.GetType().GetMethod("Clear", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(clearMethod);

        clearMethod!.Invoke(dictionary, null);
    }

    private static void ForceExpireOnboardingCooldowns()
    {
        var dictionary = GetOnboardingAttemptDictionary();
        var stateType = typeof(DeviceController).GetNestedType("OnboardingAttemptState", BindingFlags.NonPublic);
        Assert.NotNull(stateType);

        var ctor = stateType!.GetConstructors(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
            .Single(c =>
            {
                var parameters = c.GetParameters();
                return parameters.Length == 3
                    && parameters[0].ParameterType == typeof(int)
                    && parameters[1].ParameterType == typeof(DateTimeOffset)
                    && parameters[2].ParameterType == typeof(DateTimeOffset?);
            });

        var expiredState = ctor.Invoke(new object?[]
        {
            5,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddSeconds(-1)
        });

        var keysProperty = dictionary.GetType().GetProperty("Keys", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(keysProperty);

        var keys = ((System.Collections.IEnumerable)keysProperty!.GetValue(dictionary)!)
            .Cast<object>()
            .ToList();

        var indexer = dictionary.GetType().GetProperty("Item", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(indexer);

        foreach (var key in keys)
        {
            indexer!.SetValue(dictionary, expiredState, new[] { key });
        }
    }

    private static object GetOnboardingAttemptDictionary()
    {
        var field = typeof(DeviceController).GetField("FailedOnboardingAttempts", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);

        var dictionary = field!.GetValue(null);
        Assert.NotNull(dictionary);

        return dictionary!;
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
