using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace CleaningSuite.Infrastructure.Keycloak;

/// <summary>Explicit per-realm employee-extension credentials. Never falls back to platform/master Admin REST.</summary>
public sealed class TenantKeycloakClients(HttpClient http, IConfiguration configuration)
{
    private readonly ConcurrentDictionary<string, KeycloakAdminClient> _clients = new(StringComparer.Ordinal);
    public KeycloakAdminClient ForTenant(string tenantId)
    {
        if (!Regex.IsMatch(tenantId, "^[a-z0-9][a-z0-9-]{2,62}$") || tenantId == "master"
            || tenantId == configuration["Auth:Keycloak:ProvisionerRealm"])
            throw new KeycloakException("Invalid tenant identity realm");
        return _clients.GetOrAdd(tenantId, realm => {
            var section = configuration.GetSection($"Auth:Keycloak:TenantAccounts:{realm}");
            var clientId = section["ClientId"];
            var secret = section["ClientSecret"];
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret))
                throw new KeycloakException("Tenant identity service account is not configured; complete explicit identity setup before retrying");
            var server = configuration["Auth:Keycloak:AdminServerUrl"] ?? configuration["Auth:Keycloak:ServerUrl"]
                ?? throw new InvalidOperationException("Auth:Keycloak:ServerUrl missing");
            return new KeycloakAdminClient(http, server, realm, clientId, secret, allowedRealm: realm);
        });
    }
}

/// <summary>DI type boundary: never injected into employee or lead workflows.</summary>
public sealed record PlatformKeycloakClient(KeycloakAdminClient Client);
