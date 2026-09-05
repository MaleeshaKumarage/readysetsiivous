namespace CleaningSuite.Application.Tenants;

/// <summary>Current request tenant, resolved by API middleware. Empty string when unresolved.</summary>
public interface ITenantContext
{
    string TenantId { get; }
}
