using CleaningSuite.Application.Tenants;
using CleaningSuite.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Caching;

public class TenantCacheServiceTests
{
    [Fact]
    public async Task GetOrAddAsync_CachesAndReturnsValue_AndRemovesByPrefix()
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(x => x.TenantId).Returns("tenant-1");

        var cacheService = new TenantCacheService(memoryCache, tenantContextMock.Object);

        int factoryCallCount = 0;
        Task<string> Factory(CancellationToken ct)
        {
            factoryCallCount++;
            return Task.FromResult($"Value_{factoryCallCount}");
        }

        // First call executes factory
        var val1 = await cacheService.GetOrAddAsync("companies", "list", Factory);
        Assert.Equal("Value_1", val1);
        Assert.Equal(1, factoryCallCount);

        // Second call returns cached result
        var val2 = await cacheService.GetOrAddAsync("companies", "list", Factory);
        Assert.Equal("Value_1", val2);
        Assert.Equal(1, factoryCallCount);

        // Invalidate "companies" prefix
        cacheService.RemoveByPrefix("companies");

        // Subsequent call re-executes factory
        var val3 = await cacheService.GetOrAddAsync("companies", "list", Factory);
        Assert.Equal("Value_2", val3);
        Assert.Equal(2, factoryCallCount);
    }

    [Fact]
    public async Task TenantIsolation_InvalidateTenant1_DoesNotInvalidateTenant2()
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var tenantContextMock = new Mock<ITenantContext>();
        string currentTenant = "tenant-1";
        tenantContextMock.Setup(x => x.TenantId).Returns(() => currentTenant);

        var cacheService = new TenantCacheService(memoryCache, tenantContextMock.Object);

        int factory1Calls = 0;
        int factory2Calls = 0;

        currentTenant = "tenant-1";
        await cacheService.GetOrAddAsync("shifts", "list", _ => Task.FromResult($"T1_{++factory1Calls}"));

        currentTenant = "tenant-2";
        await cacheService.GetOrAddAsync("shifts", "list", _ => Task.FromResult($"T2_{++factory2Calls}"));

        // Invalidate tenant-1
        currentTenant = "tenant-1";
        cacheService.RemoveByPrefix("shifts");

        // Tenant-1 should be invalidated
        var t1Val = await cacheService.GetOrAddAsync("shifts", "list", _ => Task.FromResult($"T1_{++factory1Calls}"));
        Assert.Equal("T1_2", t1Val);

        // Tenant-2 should still be cached
        currentTenant = "tenant-2";
        var t2Val = await cacheService.GetOrAddAsync("shifts", "list", _ => Task.FromResult($"T2_{++factory2Calls}"));
        Assert.Equal("T2_1", t2Val);
    }
}
