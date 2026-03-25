using AeroponicIOT.Data;
using AeroponicIOT.Tests.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AeroponicIOT.Tests.Infrastructure;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"aeroponic-iot-tests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            var testSettings = new Dictionary<string, string?>
            {
                ["JwtSettings:SecretKey"] = "test-secret-key-at-least-32-characters-long",
                ["JwtSettings:Issuer"] = "AeroponicIOT-Test",
                ["JwtSettings:Audience"] = "AeroponicIOT-Test",
                ["JwtSettings:ExpirationMinutes"] = "60",
                ["MqttSettings:Host"] = "test-broker.local",
                ["MqttSettings:Port"] = "28883",
                ["RateLimiting:Auth:PermitLimit"] = "200",
                ["RateLimiting:Auth:WindowSeconds"] = "60",
                ["RateLimiting:DeviceOnboarding:PermitLimit"] = "500",
                ["RateLimiting:DeviceOnboarding:WindowSeconds"] = "60",
                ["Provisioning:SharedKey"] = "test-shared-key-123",
                ["Provisioning:ClaimCodeMinutes"] = "10",
                ["AppUrls:DashboardBaseUrl"] = "http://localhost:5062",
                ["OnboardingProtection:FailedAttemptThreshold"] = "5",
                ["OnboardingProtection:FailedAttemptWindowSeconds"] = "30",
                ["OnboardingProtection:FailedAttemptCooldownSeconds"] = "2",
                ["OnboardingProtection:StateTtlSeconds"] = "60",
                ["ConnectionStrings:DefaultConnection"] = "Server=(localdb)\\mssqllocaldb;Database=AeroponicIOT_Test;Trusted_Connection=True;MultipleActiveResultSets=true"
            };

            configBuilder.AddInMemoryCollection(testSettings);
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            services.RemoveAll<ApplicationDbContext>();

            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseInMemoryDatabase(_databaseName);
            });

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                options.DefaultScheme = TestAuthHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                TestAuthHandler.SchemeName,
                _ => { });
        });
    }
}
