using AeroponicIOT.Options;
using AeroponicIOT.Services.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AeroponicIOT.Tests;

public class EmailServiceTests
{
    [Fact]
    public void IsConfigured_IsFalse_WhenDisabledEvenWithValidSmtpValues()
    {
        var service = CreateService(new EmailSettingsOptions
        {
            Enabled = false,
            SmtpHost = "smtp.gmail.com",
            SmtpPort = 587,
            FromEmail = "noreply@test.local",
            FromName = "Test"
        });

        Assert.False(service.IsConfigured);
    }

    [Fact]
    public void IsConfigured_IsTrue_WhenEnabledAndRequiredValuesPresent()
    {
        var service = CreateService(new EmailSettingsOptions
        {
            Enabled = true,
            SmtpHost = "smtp.gmail.com",
            SmtpPort = 587,
            FromEmail = "noreply@test.local",
            FromName = "Test"
        });

        Assert.True(service.IsConfigured);
    }

    [Fact]
    public async Task SendEmailAsync_ReturnsFalse_WhenEmailIsDisabled()
    {
        var service = CreateService(new EmailSettingsOptions
        {
            Enabled = false,
            SmtpHost = "smtp.gmail.com",
            SmtpPort = 587,
            FromEmail = "noreply@test.local",
            FromName = "Test"
        });

        var sent = await service.SendEmailAsync("user@test.local", "subject", "<p>body</p>");

        Assert.False(sent);
    }

    private static EmailService CreateService(EmailSettingsOptions options)
    {
        return new EmailService(Microsoft.Extensions.Options.Options.Create(options), NullLogger<EmailService>.Instance);
    }
}