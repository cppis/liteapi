using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace liteapi.Services;

public class CacheService
{
    private readonly IDistributedCache _cache;
    private readonly MetricsService _metrics;
    private readonly ILogger<CacheService> _logger;
    private readonly TimeSpan _defaultTTL;

    public CacheService(
        IDistributedCache cache,
        MetricsService metrics,
        IConfiguration configuration,
        ILogger<CacheService> logger)
    {
        _cache = cache;
        _metrics = metrics;
        _logger = logger;
        _defaultTTL = TimeSpan.FromSeconds(
            configuration.GetValue<int>("Redis:DefaultTTLSeconds", 300));
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        var prefix = key.Contains(':') ? key[..key.IndexOf(':')] : key;

        try
        {
            using var timer = _metrics.TrackCacheOperationDuration("get");
            var cached = await _cache.GetStringAsync(key);
            if (cached is null)
            {
                _metrics.IncrementCacheMiss(prefix);
                return default;
            }
            _metrics.IncrementCacheHit(prefix);
            return JsonSerializer.Deserialize<T>(cached);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "캐시 읽기 실패: {Key}. DB로 폴백", key);
            _metrics.IncrementCacheMiss(prefix);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null)
    {
        try
        {
            using var timer = _metrics.TrackCacheOperationDuration("set");
            var json = JsonSerializer.Serialize(value);
            await _cache.SetStringAsync(key, json, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl ?? _defaultTTL
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "캐시 쓰기 실패: {Key}. 무시하고 계속 진행", key);
        }
    }

    public async Task RemoveAsync(string key)
    {
        try
        {
            using var timer = _metrics.TrackCacheOperationDuration("remove");
            await _cache.RemoveAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "캐시 삭제 실패: {Key}. TTL 만료에 의존", key);
        }
    }
}
