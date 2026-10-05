namespace CleaningSuite.Application.Common;

public interface ITenantCacheTokenRegistry
{
    CancellationToken GetToken(string tokenKey);
    void CancelPrefix(string tokenKey);
    void CancelTenant(string tenantPrefix);
}
