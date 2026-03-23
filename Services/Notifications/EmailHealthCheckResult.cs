namespace AeroponicIOT.Services.Notifications;

public class EmailHealthCheckResult
{
    public bool Enabled { get; init; }
    public bool IsConfigured { get; init; }
    public bool ConnectivityTested { get; init; }
    public bool CanConnect { get; init; }
    public bool CanAuthenticate { get; init; }
    public string? SmtpHost { get; init; }
    public int SmtpPort { get; init; }
    public string Message { get; init; } = string.Empty;
}
