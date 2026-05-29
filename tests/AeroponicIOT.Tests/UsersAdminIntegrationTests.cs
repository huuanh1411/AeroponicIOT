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

public class UsersAdminIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public UsersAdminIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetUsers_AsAdministrator_ReturnsUserList()
    {
        await SeedUsersAsync();

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-UserId", "99");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Administrator");

        var response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var users = payload.GetProperty("data").EnumerateArray().ToList();

        Assert.Equal(2, users.Count);
        Assert.Contains(users, u => u.GetProperty("username").GetString() == "farmer-1");
    }

    [Fact]
    public async Task GetUsers_AsFarmer_ReturnsForbidden()
    {
        await SeedUsersAsync();

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-UserId", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Farmer");

        var response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateUser_AsAdministrator_ChangesRole()
    {
        await SeedUsersAsync();

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-UserId", "99");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Administrator");

        var response = await client.PutAsJsonAsync("/api/users/1", new
        {
            email = "farmer1@test.local",
            role = "Administrator"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.FindAsync(1);

        Assert.NotNull(user);
        Assert.Equal("Administrator", user.Role);
    }

    private async Task SeedUsersAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        if (await db.Users.AnyAsync())
        {
            return;
        }

        db.Users.AddRange(
            new User { Id = 1, Username = "farmer-1", Email = "farmer1@test.local", PasswordHash = "hash", Role = "Farmer", CreatedAt = DateTime.UtcNow },
            new User { Id = 99, Username = "admin", Email = "admin@test.local", PasswordHash = "hash", Role = "Administrator", CreatedAt = DateTime.UtcNow });

        await db.SaveChangesAsync();
    }
}
