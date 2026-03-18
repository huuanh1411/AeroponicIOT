using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using AeroponicIOT.Options;
using Microsoft.Extensions.Options;

namespace AeroponicIOT.Services.Notifications;

/// <summary>
/// Email service implementation using MailKit/SMTP
/// </summary>
public class EmailService : IEmailService
{
    private readonly ILogger<EmailService> _logger;
    private readonly string? _smtpHost;
    private readonly int _smtpPort;
    private readonly string? _smtpUsername;
    private readonly string? _smtpPassword;
    private readonly string? _fromEmail;
    private readonly string? _fromName;

    public bool IsConfigured => !string.IsNullOrEmpty(_smtpHost) && !string.IsNullOrEmpty(_fromEmail);

    public EmailService(IOptions<EmailSettingsOptions> emailOptions, ILogger<EmailService> logger)
    {
        _logger = logger;

        var settings = emailOptions.Value;
        _smtpHost = settings.SmtpHost;
        _smtpPort = settings.SmtpPort;
        _smtpUsername = settings.SmtpUsername;
        _smtpPassword = settings.SmtpPassword;
        _fromEmail = settings.FromEmail;
        _fromName = settings.FromName;

        if (IsConfigured)
        {
            _logger.LogInformation("Email service configured with SMTP host: {Host}:{Port}", _smtpHost, _smtpPort);
        }
        else
        {
            _logger.LogWarning("Email service not configured. Please set EmailSettings in appsettings.json");
        }
    }

    public async Task<bool> SendEmailAsync(string to, string subject, string htmlBody, string? plainTextBody = null)
    {
        try
        {
            if (!IsConfigured)
            {
                _logger.LogWarning("Email service not configured, skipping email to {To}", to);
                return false;
            }

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_fromName, _fromEmail!));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder();
            if (!string.IsNullOrEmpty(plainTextBody))
            {
                bodyBuilder.TextBody = plainTextBody;
            }
            bodyBuilder.HtmlBody = htmlBody;
            message.Body = bodyBuilder.ToMessageBody();

            using (var client = new SmtpClient())
            {
                var secureSocketOption = _smtpPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
                
                await client.ConnectAsync(_smtpHost, _smtpPort, secureSocketOption);

                if (!string.IsNullOrEmpty(_smtpUsername) && !string.IsNullOrEmpty(_smtpPassword))
                {
                    await client.AuthenticateAsync(_smtpUsername, _smtpPassword);
                }

                await client.SendAsync(message);
                await client.DisconnectAsync(true);
            }

            _logger.LogInformation("Email sent successfully to {To} with subject: {Subject}", to, subject);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending email to {To}", to);
            return false;
        }
    }

    public async Task<bool> SendBulkEmailAsync(List<string> recipients, string subject, string htmlBody, string? plainTextBody = null)
    {
        try
        {
            if (!IsConfigured)
            {
                _logger.LogWarning("Email service not configured, skipping bulk email");
                return false;
            }

            using (var client = new SmtpClient())
            {
                var secureSocketOption = _smtpPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
                
                await client.ConnectAsync(_smtpHost, _smtpPort, secureSocketOption);

                if (!string.IsNullOrEmpty(_smtpUsername) && !string.IsNullOrEmpty(_smtpPassword))
                {
                    await client.AuthenticateAsync(_smtpUsername, _smtpPassword);
                }

                foreach (var recipient in recipients)
                {
                    try
                    {
                        var message = new MimeMessage();
                        message.From.Add(new MailboxAddress(_fromName, _fromEmail!));
                        message.To.Add(MailboxAddress.Parse(recipient));
                        message.Subject = subject;

                        var bodyBuilder = new BodyBuilder();
                        if (!string.IsNullOrEmpty(plainTextBody))
                        {
                            bodyBuilder.TextBody = plainTextBody;
                        }
                        bodyBuilder.HtmlBody = htmlBody;
                        message.Body = bodyBuilder.ToMessageBody();

                        await client.SendAsync(message);
                        _logger.LogInformation("Email sent to {Recipient}", recipient);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error sending email to {Recipient}", recipient);
                    }
                }

                await client.DisconnectAsync(true);
            }

            _logger.LogInformation("Bulk email sent to {Count} recipients", recipients.Count);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in bulk email operation");
            return false;
        }
    }
}
