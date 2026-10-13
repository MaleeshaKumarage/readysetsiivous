using Microsoft.Extensions.Configuration;

namespace CleaningSuite.Infrastructure.Auth;

/// <summary>Resource boundaries shared by validation and tenant client provisioning.</summary>
public sealed class KeycloakResourceOptions
{
    public string TenantAudience { get; }
    public string PlatformAudience { get; }
    public string ProvisionerRealm { get; }

    public KeycloakResourceOptions(IConfiguration configuration)
    {
        TenantAudience = Required(configuration, "Audience");
        PlatformAudience = Required(configuration, "PlatformAudience");
        ProvisionerRealm = configuration["Auth:Keycloak:ProvisionerRealm"] ?? "master";
        if (!System.Text.RegularExpressions.Regex.IsMatch(ProvisionerRealm, "^[a-z0-9][a-z0-9-]{2,62}$"))
            throw new InvalidOperationException("Auth:Keycloak:ProvisionerRealm is invalid");
        if (TenantAudience == PlatformAudience || new[] { TenantAudience, PlatformAudience }.Any(x => x is "account" or "cleaning-suite-web"))
            throw new InvalidOperationException("Keycloak API audiences must be distinct resources, not generic account or SPA audiences");
    }

    private static string Required(IConfiguration configuration, string name)
    {
        var value = configuration[$"Auth:Keycloak:{name}"];
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim())
            throw new InvalidOperationException($"Auth:Keycloak:{name} must be explicitly configured and nonempty");
        return value;
    }
}
