using Microsoft.Extensions.Caching.Memory;

namespace GotoRef.Api.Domain.Services;

public class CacheService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<CacheService> _logger;

    // TTLs from spec
    private static readonly TimeSpan SearchTtl    = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MetadataTtl  = TimeSpan.FromHours(24);
    private static readonly TimeSpan TypesTtl     = TimeSpan.FromDays(7);
    private static readonly TimeSpan SourceTtl    = TimeSpan.FromDays(30);

    public CacheService(IMemoryCache cache, ILogger<CacheService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<T> GetOrCreateSearchAsync<T>(string key, Func<Task<T>> factory)
        => await GetOrCreate(key, SearchTtl, factory);

    public async Task<T> GetOrCreateMetadataAsync<T>(string key, Func<Task<T>> factory)
        => await GetOrCreate(key, MetadataTtl, factory);

    public async Task<T> GetOrCreateTypesAsync<T>(string key, Func<Task<T>> factory)
        => await GetOrCreate(key, TypesTtl, factory);

    public async Task<T> GetOrCreateSourceAsync<T>(string key, Func<Task<T>> factory)
        => await GetOrCreate(key, SourceTtl, factory);

    private async Task<T> GetOrCreate<T>(string key, TimeSpan ttl, Func<Task<T>> factory)
    {
        if (_cache.TryGetValue(key, out T? cached) && cached is not null)
        {
            _logger.LogDebug("Cache hit: {Key}", key);
            return cached;
        }

        _logger.LogDebug("Cache miss: {Key}", key);
        var value = await factory();

        _cache.Set(key, value, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl,
            SlidingExpiration = null
        });

        return value;
    }
}
