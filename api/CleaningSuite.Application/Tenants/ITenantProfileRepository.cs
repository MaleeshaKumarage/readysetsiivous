using CleaningSuite.Domain.Tenants;

namespace CleaningSuite.Application.Tenants;

/// <summary>Per-tenant profile documents in the tenant's own partition.</summary>
public interface ITenantProfileRepository
{
    Task<TenantProfile?> GetAsync(string tenantId, CancellationToken ct = default);
    Task SaveAsync(string tenantId, TenantProfile profile, CancellationToken ct = default);
}
