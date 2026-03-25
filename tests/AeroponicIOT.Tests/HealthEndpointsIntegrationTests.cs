using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AeroponicIOT.Tests.Infrastructure;
using Xunit;

namespace AeroponicIOT.Tests;

public class HealthEndpointsIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public HealthEndpointsIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HealthLive_ReturnsStructuredJsonWithSelfCheck()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(payload.TryGetProperty("status", out var status));
        Assert.False(string.IsNullOrWhiteSpace(status.GetString()));

        Assert.True(payload.TryGetProperty("totalDurationMs", out _));
        Assert.True(payload.TryGetProperty("timestamp", out _));

        var checks = payload.GetProperty("checks");
        Assert.NotEqual(JsonValueKind.Undefined, checks.ValueKind);
        Assert.True(checks.GetArrayLength() >= 1);

        var hasSelf = checks.EnumerateArray().Any(c =>
            c.TryGetProperty("name", out var name) &&
            string.Equals(name.GetString(), "self", StringComparison.OrdinalIgnoreCase));

        Assert.True(hasSelf);
    }

    [Fact]
    public async Task HealthReady_ReturnsStructuredJsonWithDatabaseAndMqttChecks()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var checks = payload.GetProperty("checks").EnumerateArray().ToList();

        Assert.Contains(checks, c =>
            c.TryGetProperty("name", out var name) &&
            string.Equals(name.GetString(), "database", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(checks, c =>
            c.TryGetProperty("name", out var name) &&
            string.Equals(name.GetString(), "mqtt", StringComparison.OrdinalIgnoreCase));
    }
}
