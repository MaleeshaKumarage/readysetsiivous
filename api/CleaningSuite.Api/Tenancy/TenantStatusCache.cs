using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Tenants;
using Microsoft.Extensions.Caching.Memory;

namespace CleaningSuite.Api.Tenancy;

/// <summary>In-process cache for tenant registration status, evicted on suspend/activate.</summary>
public class TenantStatusCache : ITenantStatusCache
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public TenantRegistration? Get(string slug) =>
        _cache.TryGetValue<TenantRegistration>(slug, out var registration) ? registration : null;

    public void Set(string slug, TenantRegistration registration) =>
        _cache.Set(slug, registration, TimeSpan.FromMinutes(5));

    public void Evict(string slug) => _cache.Remove(slug);
}
