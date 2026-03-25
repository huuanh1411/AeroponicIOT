using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AeroponicIOT.Tests.Infrastructure;
using Xunit;

namespace AeroponicIOT.Tests;

public class MqttConfigurationIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public MqttConfigurationIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Status_ReturnsHostAndPortFromConfiguration()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/mqtt/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var data = payload.GetProperty("data");

        Assert.Equal("test-broker.local", data.GetProperty("host").GetString());
        Assert.Equal(28883, data.GetProperty("port").GetInt32());
        Assert.False(data.GetProperty("tlsEnabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("tlsPort").ValueKind);
        Assert.True(data.GetProperty("plaintextEndpointEnabled").GetBoolean());
        Assert.False(data.GetProperty("clientCertificateRequired").GetBoolean());
        Assert.Equal(0, data.GetProperty("clientCertificateIssuerAllowlistCount").GetInt32());
        Assert.Equal(0, data.GetProperty("clientCertificateThumbprintAllowlistCount").GetInt32());
    }
}
