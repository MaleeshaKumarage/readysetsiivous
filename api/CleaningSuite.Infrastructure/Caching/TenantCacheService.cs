using System.Collections.Concurrent;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Tenants;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace CleaningSuite.Infrastructure.Caching;

public class TenantCacheService : ITenantCacheService
{
    private readonly IMemoryCache _cache;
    private readonly ITenantContext _tenantContext;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _prefixTokens = new();

    public TenantCacheService(IMemoryCache cache, ITenantContext tenantContext)
    {
        _cache = cache;
        _tenantContext = tenantContext;
    }

    public async Task<T?> GetOrAddAsync<T>(
        string prefix,
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? expiration = null,
        CancellationToken ct = default)
    {
        var tenantId = _tenantContext.TenantId;
        var fullKey = $"tenant:{tenantId}:{prefix}:{key}";

        if (_cache.TryGetValue(fullKey, out T? cachedValue))
        {
            return cachedValue;
        }

        var result = await factory(ct);

        var tokenKey = $"tenant:{tenantId}:{prefix}";
        var cts = _prefixTokens.GetOrAdd(tokenKey, _ => new CancellationTokenSource());

        var options = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(expiration ?? TimeSpan.FromMinutes(10))
            .AddExpirationToken(new CancellationChangeToken(cts.Token));

        _cache.Set(fullKey, result, options);
        return result;
    }

    public void RemoveByPrefix(string prefix)
    {
        var tenantId = _tenantContext.TenantId;
        var tokenKey = $"tenant:{tenantId}:{prefix}";

        if (_prefixTokens.TryRemove(tokenKey, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    public void InvalidateTenant()
    {
        var tenantId = _tenantContext.TenantId;
        var tenantPrefix = $"tenant:{tenantId}:";

        foreach (var key in _prefixTokens.Keys.Where(k => k.StartsWith(tenantPrefix)).ToList())
        {
            if (_prefixTokens.TryRemove(key, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
        }
    }
}
