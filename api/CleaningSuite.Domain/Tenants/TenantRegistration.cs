using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Tenants;

/// <summary>
/// Cross-tenant catalog of all tenants. Stored in the "registry" partition.
/// Slug == Keycloak realm name == Marten tenant id.
/// </summary>
public class TenantRegistration : BaseDocument
{
    public const string StatusProvisioning = "Provisioning";
    public const string StatusActive = "Active";
    public const string StatusSuspended = "Suspended";

    public string Slug { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string[] Locales { get; set; } = ["fi", "en", "sv"];
    public string Currency { get; set; } = "EUR";
    public string Status { get; set; } = StatusProvisioning;
    public string KeycloakRealm { get; set; } = "";
}
