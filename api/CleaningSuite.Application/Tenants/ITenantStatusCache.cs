using CleaningSuite.Domain.Tenants;

namespace CleaningSuite.Application.Tenants;

/// <summary>Short-lived cache of tenant registration status, invalidated on status changes.</summary>
public interface ITenantStatusCache
{
    TenantRegistration? Get(string slug);
    void Set(string slug, TenantRegistration registration);
    void Evict(string slug);
}
