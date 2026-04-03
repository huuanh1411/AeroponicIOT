using System.Security.Claims;
using AeroponicIOT.Services.Security;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace AeroponicIOT.Tests;

public class CurrentUserServiceTests
{
    [Fact]
    public void GetCurrentUserReturnsAnonymousWhenHttpContextMissing()
    {
        var accessor = new HttpContextAccessor();
        var service = new HttpCurrentUserService(accessor);

        var currentUser = service.GetCurrentUser();

        Assert.False(currentUser.IsAuthenticated);
        Assert.Null(currentUser.UserId);
        Assert.Null(currentUser.Role);
    }

    [Fact]
    public void GetCurrentUserResolvesNameIdentifierAndRoleClaims()
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "42"),
                new Claim(ClaimTypes.Role, "Administrator")
            ],
            authenticationType: "TestAuth");

        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity)
        };

        var accessor = new HttpContextAccessor
        {
            HttpContext = context
        };

        var service = new HttpCurrentUserService(accessor);
        var currentUser = service.GetCurrentUser();

        Assert.True(currentUser.IsAuthenticated);
        Assert.Equal(42, currentUser.UserId);
        Assert.True(currentUser.IsAdministrator);
    }

    [Fact]
    public void GetCurrentUserFallsBackToSubAndRoleClaims()
    {
        var identity = new ClaimsIdentity(
            [
                new Claim("sub", "7"),
                new Claim("role", "Farmer")
            ],
            authenticationType: "TestAuth");

        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity)
        };

        var accessor = new HttpContextAccessor
        {
            HttpContext = context
        };

        var service = new HttpCurrentUserService(accessor);
        var currentUser = service.GetCurrentUser();

        Assert.True(currentUser.IsAuthenticated);
        Assert.Equal(7, currentUser.UserId);
        Assert.Equal("Farmer", currentUser.Role);
        Assert.False(currentUser.IsAdministrator);
    }
}
