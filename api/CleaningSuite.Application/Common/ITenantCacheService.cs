namespace CleaningSuite.Application.Common;

public interface ITenantCacheService
{
    Task<T?> GetOrAddAsync<T>(
        string prefix,
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? expiration = null,
        CancellationToken ct = default);

    void RemoveByPrefix(string prefix);
    void InvalidateTenant();
}
