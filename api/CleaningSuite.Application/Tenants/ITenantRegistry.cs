using CleaningSuite.Domain.Tenants;

namespace CleaningSuite.Application.Tenants;

/// <summary>Reads/writes the cross-tenant registry partition.</summary>
public interface ITenantRegistry
{
    Task<TenantRegistration?> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<TenantRegistration?> GetByRealmAsync(string realm, CancellationToken ct = default);
    Task SaveAsync(TenantRegistration registration, CancellationToken ct = default);
    Task<IReadOnlyList<TenantRegistration>> ListAsync(CancellationToken ct = default);
}
