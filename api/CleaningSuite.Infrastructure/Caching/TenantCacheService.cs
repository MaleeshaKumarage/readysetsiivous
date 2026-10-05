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
    private readonly ITenantCacheTokenRegistry _tokenRegistry;
    private static readonly ConcurrentDictionary<string, object> _inflightTasks = new();

    public TenantCacheService(
        IMemoryCache cache,
        ITenantContext tenantContext,
        ITenantCacheTokenRegistry tokenRegistry)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _tokenRegistry = tokenRegistry ?? throw new ArgumentNullException(nameof(tokenRegistry));
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

        var tokenKey = $"tenant:{tenantId}:{prefix}";
        var token = _tokenRegistry.GetToken(tokenKey);

        var lazyTask = (Lazy<Task<T>>)_inflightTasks.GetOrAdd(
            fullKey,
            _ => new Lazy<Task<T>>(() => factory(CancellationToken.None), LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            var result = await lazyTask.Value;

            if (!token.IsCancellationRequested && result is not null)
            {
                var options = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(expiration ?? TimeSpan.FromMinutes(10))
                    .AddExpirationToken(new CancellationChangeToken(token));

                _cache.Set(fullKey, result, options);
            }

            return result;
        }
        finally
        {
            _inflightTasks.TryRemove(fullKey, out _);
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
