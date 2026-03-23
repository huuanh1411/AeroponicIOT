using AeroponicIOT.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace AeroponicIOT.Tests;

public class SecurityAttributesTests
{
    [Fact]
    public void NotificationTestEmail_RequiresAdminPolicy()
    {
        var method = typeof(NotificationController).GetMethod("TestEmailNotification");
        Assert.NotNull(method);

        var attribute = method!.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .SingleOrDefault(a => a.Policy == "AdminOnly");

        Assert.NotNull(attribute);
    }

    [Fact]
    public void AuthenticationRegister_HasRateLimiterPolicy()
    {
        var method = typeof(AuthenticationController).GetMethod("Register");
        Assert.NotNull(method);

        var attribute = method!.GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: true)
            .Cast<EnableRateLimitingAttribute>()
            .SingleOrDefault(a => a.PolicyName == "auth");

        Assert.NotNull(attribute);
    }

    [Fact]
    public void AuthenticationLogin_HasRateLimiterPolicy()
    {
        var method = typeof(AuthenticationController).GetMethod("Login");
        Assert.NotNull(method);

        var attribute = method!.GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: true)
            .Cast<EnableRateLimitingAttribute>()
            .SingleOrDefault(a => a.PolicyName == "auth");

        Assert.NotNull(attribute);
    }

    [Fact]
    public void AuthenticationMe_RequiresAuthorization()
    {
        var method = typeof(AuthenticationController).GetMethod("GetCurrentUser");
        Assert.NotNull(method);

        var hasAuthorize = method!.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Any();

        Assert.True(hasAuthorize);
    }

    [Fact]
    public void DevicePending_RequiresAdminPolicy()
    {
        var method = typeof(DeviceController).GetMethod("GetPendingDevices");
        Assert.NotNull(method);

        var attribute = method!.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .SingleOrDefault(a => a.Policy == "AdminOnly");

        Assert.NotNull(attribute);
    }

    [Fact]
    public void DeviceSelfRegister_HasOnboardingRateLimiterPolicy()
    {
        var method = typeof(DeviceController).GetMethod("SelfRegister");
        Assert.NotNull(method);

        var attribute = method!.GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: true)
            .Cast<EnableRateLimitingAttribute>()
            .SingleOrDefault(a => a.PolicyName == "device-onboarding");

        Assert.NotNull(attribute);
    }

    [Fact]
    public void DeviceClaim_HasOnboardingRateLimiterPolicy()
    {
        var method = typeof(DeviceController).GetMethod("ClaimDevice");
        Assert.NotNull(method);

        var attribute = method!.GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: true)
            .Cast<EnableRateLimitingAttribute>()
            .SingleOrDefault(a => a.PolicyName == "device-onboarding");

        Assert.NotNull(attribute);
    }
}
