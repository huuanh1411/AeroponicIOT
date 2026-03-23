using System.ComponentModel.DataAnnotations;
using AeroponicIOT.Controllers;
using AeroponicIOT.DTOs;
using Xunit;

namespace AeroponicIOT.Tests;

public class DtoValidationTests
{
    [Fact]
    public void UpdateDeviceDto_StatusRejectsUnknownValue()
    {
        var dto = new UpdateDeviceDto
        {
            Status = "broken"
        };

        var results = Validate(dto);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(UpdateDeviceDto.Status)));
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("active")]
    [InlineData("ONLINE")]
    [InlineData("offline")]
    [InlineData("Inactive")]
    public void UpdateDeviceDto_StatusAcceptsAllowedValues(string status)
    {
        var dto = new UpdateDeviceDto
        {
            Status = status
        };

        var results = Validate(dto);

        Assert.Empty(results);
    }

    [Fact]
    public void LoginRequest_RejectsWhitespaceOnlyUsername()
    {
        var dto = new LoginRequest
        {
            Username = "   ",
            Password = "ValidPass1"
        };

        var results = Validate(dto);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(LoginRequest.Username)));
    }

    [Fact]
    public void RegisterRequest_RejectsWhitespaceOnlyPassword()
    {
        var dto = new RegisterRequest
        {
            Username = "valid-user",
            Email = "valid@test.local",
            Password = "        "
        };

        var results = Validate(dto);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(RegisterRequest.Password)));
    }

    [Fact]
    public void PublishRequest_RejectsWhitespaceTopic()
    {
        var dto = new PublishRequest
        {
            Topic = "   ",
            Payload = "{\"ok\":true}"
        };

        var results = Validate(dto);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(PublishRequest.Topic)));
    }

    [Fact]
    public void PublishRequest_RejectsWhitespacePayload()
    {
        var dto = new PublishRequest
        {
            Topic = "devices/test",
            Payload = "    "
        };

        var results = Validate(dto);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(PublishRequest.Payload)));
    }

    private static IReadOnlyList<ValidationResult> Validate(object model)
    {
        var context = new ValidationContext(model);
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(model, context, results, validateAllProperties: true);

        return results;
    }
}
