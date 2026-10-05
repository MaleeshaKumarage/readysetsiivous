using CleaningSuite.Application.Common;
using CleaningSuite.Application.Tenants;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace CleaningSuite.Infrastructure.Caching;

public class TenantCacheService : ITenantCacheService
{
    private readonly IMemoryCache _cache;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantCacheTokenRegistry _tokenRegistry;

    private static readonly SemaphoreSlim[] _locks = Enumerable.Range(0, 64)
        .Select(_ => new SemaphoreSlim(1, 1))
        .ToArray();

    public TenantCacheService(
        IMemoryCache cache,
        ITenantContext tenantContext,
        ITenantCacheTokenRegistry? tokenRegistry = null)
    {
        _cache = cache;
        _tenantContext = tenantContext;
        _tokenRegistry = tokenRegistry ?? new TenantCacheTokenRegistry();
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

        var lockIndex = Math.Abs(fullKey.GetHashCode()) % _locks.Length;
        var keyLock = _locks[lockIndex];

        await keyLock.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(fullKey, out cachedValue))
            {
                return cachedValue;
            }

            var tokenKey = $"tenant:{tenantId}:{prefix}";
            var token = _tokenRegistry.GetToken(tokenKey);

            var result = await factory(ct);

            if (token.IsCancellationRequested || result is null)
            {
                return result;
            }

            var options = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(expiration ?? TimeSpan.FromMinutes(10))
                .AddExpirationToken(new CancellationChangeToken(token));

            _cache.Set(fullKey, result, options);
            return result;
        }
        finally
        {
            keyLock.Release();
        }
    }

    public void RemoveByPrefix(string prefix)
    {
        var tenantId = _tenantContext.TenantId;
        var tokenKey = $"tenant:{tenantId}:{prefix}";
        _tokenRegistry.CancelPrefix(tokenKey);
    }

    public void InvalidateTenant()
    {
        var tenantId = _tenantContext.TenantId;
        var tenantPrefix = $"tenant:{tenantId}:";
        _tokenRegistry.CancelTenant(tenantPrefix);
    }
}
