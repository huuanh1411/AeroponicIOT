using System.Text.Json;
using AeroponicIOT.Options;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace AeroponicIOT.Services.Security;

public sealed class DistributedOnboardingAttemptTracker : IOnboardingAttemptTracker
{
    private readonly IDistributedCache _cache;
    private readonly OnboardingProtectionOptions _options;

    public DistributedOnboardingAttemptTracker(
        IDistributedCache cache,
        IOptions<OnboardingProtectionOptions> options)
    {
        _cache = cache;
        _options = options.Value;
    }

    public async Task<int?> GetCooldownRemainingSecondsAsync(string attemptKey, CancellationToken cancellationToken = default)
    {
        var state = await GetStateAsync(attemptKey, cancellationToken);
        if (state == null || !state.BlockedUntilUtc.HasValue)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        if (state.BlockedUntilUtc.Value <= now)
        {
            await _cache.RemoveAsync(attemptKey, cancellationToken);
            return null;
        }

        return (int)Math.Ceiling((state.BlockedUntilUtc.Value - now).TotalSeconds);
    }

    public async Task RegisterFailedAttemptAsync(string attemptKey, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var state = await GetStateAsync(attemptKey, cancellationToken)
            ?? new OnboardingAttemptState(0, now, null);

        if (state.BlockedUntilUtc.HasValue && state.BlockedUntilUtc.Value > now)
        {
            return;
        }

        var windowStart = state.WindowStartUtc;
        var count = state.Count;

        if (now - windowStart > TimeSpan.FromSeconds(_options.FailedAttemptWindowSeconds))
        {
            windowStart = now;
            count = 0;
        }

        count++;
        DateTimeOffset? blockedUntil = count >= _options.FailedAttemptThreshold
            ? now.AddSeconds(_options.FailedAttemptCooldownSeconds)
            : null;

        var updated = new OnboardingAttemptState(count, windowStart, blockedUntil);
        await SetStateAsync(attemptKey, updated, cancellationToken);
    }

    public Task ResetAsync(string attemptKey, CancellationToken cancellationToken = default)
    {
        return _cache.RemoveAsync(attemptKey, cancellationToken);
    }

    private async Task<OnboardingAttemptState?> GetStateAsync(string attemptKey, CancellationToken cancellationToken)
    {
        var payload = await _cache.GetStringAsync(attemptKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        return JsonSerializer.Deserialize<OnboardingAttemptState>(payload);
    }

    private Task SetStateAsync(string attemptKey, OnboardingAttemptState state, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(state);

        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(_options.StateTtlSeconds)
        };

        return _cache.SetStringAsync(attemptKey, payload, options, cancellationToken);
    }

    private sealed record OnboardingAttemptState(int Count, DateTimeOffset WindowStartUtc, DateTimeOffset? BlockedUntilUtc);
}
