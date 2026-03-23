namespace AeroponicIOT.Services.Notifications;

/// <summary>
/// Interface for email sending functionality
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Send an email
    /// </summary>
    Task<bool> SendEmailAsync(string to, string subject, string htmlBody, string? plainTextBody = null);

    /// <summary>
    /// Send an email to multiple recipients
    /// </summary>
    Task<bool> SendBulkEmailAsync(List<string> recipients, string subject, string htmlBody, string? plainTextBody = null);

    /// <summary>
    /// Check SMTP configuration and optionally test connectivity/authentication.
    /// </summary>
    Task<EmailHealthCheckResult> CheckHealthAsync(bool testConnectivity = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if email service is configured
    /// </summary>
    bool IsConfigured { get; }
}
