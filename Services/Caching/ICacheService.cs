namespace AeroponicIOT.Services.Caching;

/// <summary>
/// Cache abstraction for distributed caching (Redis)
/// Provides get/set operations with TTL support
/// </summary>
public interface ICacheService
{
    /// <summary>Get a cached value by key (null if not found)</summary>
    Task<T?> GetAsync<T>(string key);

    /// <summary>Set a cached value with TTL</summary>
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null);

    /// <summary>Remove a cached value by key</summary>
    Task RemoveAsync(string key);

    /// <summary>Remove all cached values matching a pattern</summary>
    Task RemoveByPatternAsync(string pattern);

    /// <summary>Check if a key exists in cache</summary>
    Task<bool> ExistsAsync(string key);
}
