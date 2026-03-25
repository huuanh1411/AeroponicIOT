using System.ComponentModel.DataAnnotations;

namespace AeroponicIOT.Options;

public class OnboardingProtectionOptions
{
    [Range(1, 100)]
    public int FailedAttemptThreshold { get; set; } = 5;

    [Range(1, 3600)]
    public int FailedAttemptWindowSeconds { get; set; } = 300;

    [Range(1, 3600)]
    public int FailedAttemptCooldownSeconds { get; set; } = 120;

    [Range(30, 86400)]
    public int StateTtlSeconds { get; set; } = 900;
}
