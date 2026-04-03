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
    private readonly bool _enabled;
    private readonly string? _smtpHost;
    private readonly int _smtpPort;
    private readonly string? _smtpUsername;
    private readonly string? _smtpPassword;
    private readonly string? _fromEmail;
    private readonly string? _fromName;

    private static readonly Action<ILogger, Exception?> LogInfoServiceDisabled =
        LoggerMessage.Define(LogLevel.Information, new EventId(1, nameof(EmailService)), "Email service is disabled via EmailSettings:Enabled");

    private static readonly Action<ILogger, string?, int, Exception?> LogInfoServiceConfigured =
        LoggerMessage.Define<string?, int>(LogLevel.Information, new EventId(2, nameof(EmailService)), "Email service configured with SMTP host: {Host}:{Port}");

    private static readonly Action<ILogger, Exception?> LogWarningServiceMisconfigured =
        LoggerMessage.Define(LogLevel.Warning, new EventId(3, nameof(EmailService)), "Email service not configured. Please set EmailSettings in appsettings.json");

    private static readonly Action<ILogger, string, Exception?> LogWarningSkippingEmailToRecipient =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(4, nameof(SendEmailAsync)), "Email service not configured, skipping email to {To}");

    private static readonly Action<ILogger, string, string, Exception?> LogInfoEmailSent =
        LoggerMessage.Define<string, string>(LogLevel.Information, new EventId(5, nameof(SendEmailAsync)), "Email sent successfully to {To} with subject: {Subject}");

    private static readonly Action<ILogger, string, Exception?> LogErrorSendingEmailToRecipient =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(6, nameof(SendEmailAsync)), "Error sending email to {To}");

    private static readonly Action<ILogger, Exception?> LogWarningSkippingBulkEmail =
        LoggerMessage.Define(LogLevel.Warning, new EventId(7, nameof(SendBulkEmailAsync)), "Email service not configured, skipping bulk email");

    private static readonly Action<ILogger, string, Exception?> LogInfoBulkEmailRecipientSent =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(8, nameof(SendBulkEmailAsync)), "Email sent to {Recipient}");

    private static readonly Action<ILogger, string, Exception?> LogErrorBulkEmailRecipientFailed =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(9, nameof(SendBulkEmailAsync)), "Error sending email to {Recipient}");

    private static readonly Action<ILogger, int, Exception?> LogInfoBulkEmailCompleted =
        LoggerMessage.Define<int>(LogLevel.Information, new EventId(10, nameof(SendBulkEmailAsync)), "Bulk email sent to {Count} recipients");

    private static readonly Action<ILogger, Exception?> LogErrorBulkEmailOperationFailed =
        LoggerMessage.Define(LogLevel.Error, new EventId(11, nameof(SendBulkEmailAsync)), "Error in bulk email operation");

    private static readonly Action<ILogger, string?, int, Exception?> LogErrorHealthCheckFailed =
        LoggerMessage.Define<string?, int>(LogLevel.Error, new EventId(12, nameof(CheckHealthAsync)), "Email health check failed for SMTP host {Host}:{Port}");

    public bool IsConfigured => _enabled && !string.IsNullOrEmpty(_smtpHost) && !string.IsNullOrEmpty(_fromEmail);

    public EmailService(IOptions<EmailSettingsOptions> emailOptions, ILogger<EmailService> logger)
    {
        _logger = logger;

        var settings = emailOptions.Value;
        _enabled = settings.Enabled;
        _smtpHost = settings.SmtpHost;
        _smtpPort = settings.SmtpPort;
        _smtpUsername = settings.SmtpUsername;
        _smtpPassword = settings.SmtpPassword;
        _fromEmail = settings.FromEmail;
        _fromName = settings.FromName;

        if (!_enabled)
        {
            LogInfoServiceDisabled(_logger, null);
        }
        else if (IsConfigured)
        {
            LogInfoServiceConfigured(_logger, _smtpHost, _smtpPort, null);
        }
        else
        {
            LogWarningServiceMisconfigured(_logger, null);
        }
    }

    public async Task<bool> SendEmailAsync(string to, string subject, string htmlBody, string? plainTextBody = null)
    {
        try
        {
            if (!IsConfigured)
            {
                LogWarningSkippingEmailToRecipient(_logger, to, null);
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

            LogInfoEmailSent(_logger, to, subject, null);
            return true;
        }
        catch (Exception ex)
        {
            LogErrorSendingEmailToRecipient(_logger, to, ex);
            return false;
        }
    }

    public async Task<bool> SendBulkEmailAsync(List<string> recipients, string subject, string htmlBody, string? plainTextBody = null)
    {
        try
        {
            if (!IsConfigured)
            {
                LogWarningSkippingBulkEmail(_logger, null);
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
                        LogInfoBulkEmailRecipientSent(_logger, recipient, null);
                    }
                    catch (Exception ex)
                    {
                        LogErrorBulkEmailRecipientFailed(_logger, recipient, ex);
                    }
                }

                await client.DisconnectAsync(true);
            }

            LogInfoBulkEmailCompleted(_logger, recipients.Count, null);
            return true;
        }
        catch (Exception ex)
        {
            LogErrorBulkEmailOperationFailed(_logger, ex);
            return false;
        }
    }

    public async Task<EmailHealthCheckResult> CheckHealthAsync(bool testConnectivity = true, CancellationToken cancellationToken = default)
    {
        if (!_enabled)
        {
            return new EmailHealthCheckResult
            {
                Enabled = false,
                IsConfigured = false,
                ConnectivityTested = false,
                CanConnect = false,
                CanAuthenticate = false,
                SmtpHost = _smtpHost,
                SmtpPort = _smtpPort,
                Message = "Email sending is disabled (EmailSettings:Enabled = false)."
            };
        }

        if (!IsConfigured)
        {
            return new EmailHealthCheckResult
            {
                Enabled = true,
                IsConfigured = false,
                ConnectivityTested = false,
                CanConnect = false,
                CanAuthenticate = false,
                SmtpHost = _smtpHost,
                SmtpPort = _smtpPort,
                Message = "Email settings are incomplete. Configure SMTP host and from email."
            };
        }

        if (!testConnectivity)
        {
            return new EmailHealthCheckResult
            {
                Enabled = true,
                IsConfigured = true,
                ConnectivityTested = false,
                CanConnect = false,
                CanAuthenticate = false,
                SmtpHost = _smtpHost,
                SmtpPort = _smtpPort,
                Message = "Email is enabled and configured. Connectivity test skipped."
            };
        }

        try
        {
            using var client = new SmtpClient();
            var secureSocketOption = _smtpPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;

            await client.ConnectAsync(_smtpHost, _smtpPort, secureSocketOption, cancellationToken);

            var canAuthenticate = false;
            if (!string.IsNullOrWhiteSpace(_smtpUsername) && !string.IsNullOrWhiteSpace(_smtpPassword))
            {
                await client.AuthenticateAsync(_smtpUsername, _smtpPassword, cancellationToken);
                canAuthenticate = true;
            }

            await client.DisconnectAsync(true, cancellationToken);

            return new EmailHealthCheckResult
            {
                Enabled = true,
                IsConfigured = true,
                ConnectivityTested = true,
                CanConnect = true,
                CanAuthenticate = canAuthenticate,
                SmtpHost = _smtpHost,
                SmtpPort = _smtpPort,
                Message = canAuthenticate
                    ? "SMTP connectivity and authentication succeeded."
                    : "SMTP connectivity succeeded (no SMTP credentials configured)."
            };
        }
        catch (Exception ex)
        {
            LogErrorHealthCheckFailed(_logger, _smtpHost, _smtpPort, ex);
            return new EmailHealthCheckResult
            {
                Enabled = true,
                IsConfigured = true,
                ConnectivityTested = true,
                CanConnect = false,
                CanAuthenticate = false,
                SmtpHost = _smtpHost,
                SmtpPort = _smtpPort,
                Message = $"SMTP health check failed: {ex.Message}"
            };
        }
    }
}
