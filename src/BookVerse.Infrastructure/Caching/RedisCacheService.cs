using System.Text.Json;
using BookVerse.Application.Common.Interfaces;
using BookVerse.Infrastructure.Observability;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace BookVerse.Infrastructure.Caching;

public class RedisCacheService : ICacheService
{
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<RedisCacheService> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public RedisCacheService(ILogger<RedisCacheService> logger, IConnectionMultiplexer? redis = null)
    {
        _logger = logger;
        _redis = redis;
    }

    private IDatabase? GetDatabase()
    {
        try
        {
            if (_redis != null && _redis.IsConnected)
            {
                return _redis.GetDatabase();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect to Redis. Caching degraded to fallback.");
        }
        return null;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var db = GetDatabase();
        if (db == null)
        {
            BookVerseMetrics.CacheUnavailable.Add(1);
            return default;
        }

        try
        {
            var value = await db.StringGetAsync(key);
            if (value.IsNullOrEmpty)
            {
                BookVerseMetrics.CacheMisses.Add(1);
                return default;
            }

            var result = JsonSerializer.Deserialize<T>(value.ToString(), _jsonOptions);
            BookVerseMetrics.CacheHits.Add(1);
            return result;
        }
        catch (Exception ex)
        {
            BookVerseMetrics.CacheFailures.Add(1);
            _logger.LogWarning(ex, "Redis error reading key {Key}. Falling back gracefully.", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        var db = GetDatabase();
        if (db == null) return;

        try
        {
            var json = JsonSerializer.Serialize(value, _jsonOptions);
            if (expiration.HasValue)
            {
                await db.StringSetAsync(key, json, expiration.Value);
            }
            else
            {
                await db.StringSetAsync(key, json);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis error setting key {Key}. Continuing without cache.", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var db = GetDatabase();
        if (db == null) return;

        try
        {
            await db.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis error deleting key {Key}.", key);
        }
    }
}
