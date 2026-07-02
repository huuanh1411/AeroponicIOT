using StackExchange.Redis;
using System.Text.Json;

namespace AeroponicIOT.Services.Caching;

/// <summary>
/// Redis-backed distributed cache service
/// Enables sharing cached data across multiple API instances
/// </summary>
public class RedisCacheService : ICacheService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisCacheService> _logger;
    private IDatabase? _db;

    public RedisCacheService(IConnectionMultiplexer redis, ILogger<RedisCacheService> logger)
    {
        _redis = redis;
        _logger = logger;
        try
        {
            _db = _redis.GetDatabase();
            _logger.LogInformation("Redis cache service initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Redis connection");
            _db = null;
        }
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        if (_db == null)
            return default;

        try
        {
            var value = await _db.StringGetAsync(key);
            if (value.IsNull)
                return default;

            return JsonSerializer.Deserialize<T>(value.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving cache key: {CacheKey}", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null)
    {
        if (_db == null)
            return;

        try
        {
            var serialized = JsonSerializer.Serialize(value);
            await _db.StringSetAsync(key, serialized, expiration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting cache key: {CacheKey}", key);
        }
    }

    public async Task RemoveAsync(string key)
    {
        if (_db == null)
            return;

        try
        {
            await _db.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cache key: {CacheKey}", key);
        }
    }

    public async Task RemoveByPatternAsync(string pattern)
    {
        if (_db == null)
            return;

        try
        {
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            var keys = server.Keys(pattern: pattern);
            
            foreach (var key in keys)
            {
                await _db.KeyDeleteAsync(key);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cache by pattern: {Pattern}", pattern);
        }
    }

    public async Task<bool> ExistsAsync(string key)
    {
        if (_db == null)
            return false;

        try
        {
            return await _db.KeyExistsAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking cache key existence: {CacheKey}", key);
            return false;
        }
    }
}
