using System.Collections.Concurrent;
using CleaningSuite.Application.Common;

namespace CleaningSuite.Infrastructure.Caching;

public class TenantCacheTokenRegistry : ITenantCacheTokenRegistry
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _prefixTokens = new();

    public CancellationToken GetToken(string tokenKey)
    {
        var cts = _prefixTokens.GetOrAdd(tokenKey, _ => new CancellationTokenSource());
        return cts.Token;
    }

    public void CancelPrefix(string tokenKey)
    {
        if (_prefixTokens.TryRemove(tokenKey, out var cts))
        {
            cts.Cancel();
        }
    }

    public void CancelTenant(string tenantPrefix)
    {
        foreach (var key in _prefixTokens.Keys.Where(k => k.StartsWith(tenantPrefix)).ToList())
        {
            if (_prefixTokens.TryRemove(key, out var cts))
            {
                cts.Cancel();
            }
        }
    }
}
