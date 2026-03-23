using AeroponicIOT.Data;
using AeroponicIOT.Models;
using AeroponicIOT.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AeroponicIOT.Tests;

public class SchemaConstraintsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public SchemaConstraintsTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void IdentityAndProvisioningIndexes_AreConfiguredAsUnique()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var userEntity = db.Model.FindEntityType(typeof(User));
        Assert.NotNull(userEntity);

        var usernameIndex = userEntity!.GetIndexes().Single(i => i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(User.Username) }));
        var emailIndex = userEntity.GetIndexes().Single(i => i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(User.Email) }));

        Assert.True(usernameIndex.IsUnique);
        Assert.Equal("[username] IS NOT NULL", usernameIndex.GetFilter());

        Assert.True(emailIndex.IsUnique);
        Assert.Equal("[email] IS NOT NULL", emailIndex.GetFilter());

        var deviceEntity = db.Model.FindEntityType(typeof(Device));
        Assert.NotNull(deviceEntity);

        var macAddressIndex = deviceEntity!.GetIndexes().Single(i => i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(Device.MacAddress) }));
        var claimCodeIndex = deviceEntity.GetIndexes().Single(i => i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(Device.ClaimCode) }));

        Assert.True(macAddressIndex.IsUnique);

        Assert.True(claimCodeIndex.IsUnique);
        Assert.Equal("[claim_code] IS NOT NULL", claimCodeIndex.GetFilter());
    }

    [Fact]
    public void DeviceUserForeignKey_DoesNotUseShadowUserId1()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var deviceEntity = db.Model.FindEntityType(typeof(Device));
        Assert.NotNull(deviceEntity);

        Assert.DoesNotContain(deviceEntity!.GetProperties(), p => p.Name == "UserId1");

        var userFks = deviceEntity
            .GetForeignKeys()
            .Where(fk => fk.PrincipalEntityType.ClrType == typeof(User))
            .ToList();

        Assert.Single(userFks);
        Assert.Equal(nameof(Device.UserId), userFks[0].Properties.Single().Name);
    }

    [Fact]
    public void UniqueConstraintsMigration_IsPresentInAssembly()
    {
        var migrationType = typeof(ApplicationDbContext).Assembly.GetType("AeroponicIOT.Migrations.AddUniqueConstraintsAndFixDeviceUserFk");

        Assert.NotNull(migrationType);
        Assert.True(typeof(Migration).IsAssignableFrom(migrationType));
    }
}
