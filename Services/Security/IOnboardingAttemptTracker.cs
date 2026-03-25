namespace AeroponicIOT.Services.Security;

public interface IOnboardingAttemptTracker
{
    Task<int?> GetCooldownRemainingSecondsAsync(string attemptKey, CancellationToken cancellationToken = default);
    Task RegisterFailedAttemptAsync(string attemptKey, CancellationToken cancellationToken = default);
    Task ResetAsync(string attemptKey, CancellationToken cancellationToken = default);
}
