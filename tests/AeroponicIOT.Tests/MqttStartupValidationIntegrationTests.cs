using AeroponicIOT.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AeroponicIOT.Tests;

public class MqttStartupValidationIntegrationTests
{
    [Fact]
    public void Startup_Fails_WhenClientCertificateRequiredWithoutAllowlist()
    {
        using var factory = new InvalidMtlsConfigurationFactory();

        var exception = Assert.ThrowsAny<Exception>(() =>
        {
            using var client = factory.CreateClient();
        });

        Assert.Contains(
            "AllowedClientCertificateIssuers or MqttSettings:AllowedClientCertificateThumbprints",
            exception.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed class InvalidMtlsConfigurationFactory : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                var invalidMqttSettings = new Dictionary<string, string?>
                {
                    ["MqttSettings:EnableTls"] = "true",
                    ["MqttSettings:RequireClientCertificate"] = "true",
                    ["MqttSettings:ServerCertificatePath"] = "C:/tmp/dummy-cert.pfx"
                };

                configBuilder.AddInMemoryCollection(invalidMqttSettings);
            });
        }
    }
}