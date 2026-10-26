using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Infrastructure.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace CleaningSuite.Infrastructure.Keycloak;

public class KeycloakProvisioner : IKeycloakProvisioner
{
    private const string ClientId = "cleaning-suite-web";
    private readonly KeycloakAdminClient _admin;
    private readonly ILogger<KeycloakProvisioner> _logger;
    private readonly KeycloakResourceOptions _resources;
    private readonly IConfiguration? _configuration;

    public KeycloakProvisioner(PlatformKeycloakClient admin, ILogger<KeycloakProvisioner> logger, KeycloakResourceOptions resources, IConfiguration? configuration = null)
    {
        _admin = admin.Client;
        _logger = logger;
        _resources = resources;
        _configuration = configuration;
    }

    public async Task<ProvisionedTenant> ProvisionAsync(
        string slug,
        string companyName,
        string ownerEmail,
        string ownerFirstName,
        string ownerLastName,
        string[] redirectUris,
        CancellationToken ct = default)
    {
        if (!Regex.IsMatch(slug, "^[a-z0-9][a-z0-9-]{2,62}$") || slug == "master"
            || slug == _configuration?["Auth:Keycloak:ProvisionerRealm"])
            throw new TenantIdentitySetupRequiredException("Invalid tenant identity realm");
        if (_configuration is not null && !_configuration.GetValue<bool>("Auth:Keycloak:ProvisioningSetupReviewed"))
            throw new TenantIdentitySetupRequiredException("New tenant provisioning is paused until scoped identity account setup and secret delivery have been reviewed. No realm was created.");
        var runtime = _configuration?.GetSection($"Auth:Keycloak:TenantAccounts:{slug}");
        if (_configuration is not null && (string.IsNullOrWhiteSpace(runtime?["ClientId"]) || string.IsNullOrWhiteSpace(runtime?["ClientSecret"])))
            throw new TenantIdentitySetupRequiredException("Prepare the new tenant's scoped service-account secret in protected API configuration before provisioning. No realm was created.");
        if (_configuration is not null && (string.IsNullOrWhiteSpace(_configuration["EmployeeInvites:Smtp:Host"])
            || string.IsNullOrWhiteSpace(_configuration["EmployeeInvites:Smtp:From"]) || string.IsNullOrWhiteSpace(_configuration["Auth:Keycloak:ServerUrl"])))
            throw new TenantIdentitySetupRequiredException("Prepare invitation SMTP and public identity URL before provisioning. No realm was created.");
        if (runtime is not null && (!Regex.IsMatch(runtime["ClientId"]!, "^[a-z0-9][a-z0-9-]{2,62}$")
            || runtime["ClientId"] is "realm-management" or "cleaning-suite-web" or "account" or "admin-cli"))
            throw new TenantIdentitySetupRequiredException("Use a dedicated confidential tenant identity client. No realm was created.");
        if (_configuration is not null && _configuration.GetValue("EmployeeInvites:Smtp:Auth", true)
            && (string.IsNullOrWhiteSpace(_configuration["EmployeeInvites:Smtp:User"])
                || string.IsNullOrWhiteSpace(_configuration.GetValue<bool>("EmployeeInvites:Smtp:UseResendApiKey")
                    ? _configuration["Resend:ApiKey"] : _configuration["EmployeeInvites:Smtp:Password"])))
            throw new TenantIdentitySetupRequiredException("Prepare invitation SMTP authentication before provisioning. No realm was created.");
        var temporaryPassword = GenerateTemporaryPassword();

        await _admin.CreateRealmAsync(slug, ct);
        try
        {
            await WaitForRealmAsync(slug, ct);

            await _admin.CreateRoleAsync(slug, "admin", ct);
            await _admin.CreateRoleAsync(slug, "employee", ct);
            await _admin.CreateRoleAsync(slug, "manager", ct);

            await _admin.CreateClientAsync(slug, new
            {
                clientId = ClientId,
                name = $"{companyName} web",
                enabled = true,
                publicClient = true,
                standardFlowEnabled = true,
                directAccessGrantsEnabled = true,
                redirectUris,
                webOrigins = new[] { "+" },
                protocolMappers = new[] { AudienceMapper(_resources.TenantAudience) },
            }, ct);

            if (runtime is not null)
                await ConfigureRuntimeAccountAsync(slug, runtime["ClientId"]!, runtime["ClientSecret"]!, ct);

            var userId = await _admin.CreateUserAsync(slug, new
            {
                username = ownerEmail,
                email = ownerEmail,
                firstName = ownerFirstName,
                lastName = ownerLastName,
                enabled = true,
                credentials = new[]
                {
                    new { type = "password", value = temporaryPassword, temporary = true },
                },
                requiredActions = new[] { "UPDATE_PASSWORD" },
            }, ct);

            var adminRole = await _admin.GetRoleAsync(slug, "admin", ct);
            await _admin.AssignRealmRoleAsync(slug, userId, new object[] { adminRole }, ct);

            _logger.LogInformation("Provisioned tenant realm {Realm} for {Company}", slug, companyName);

            return new ProvisionedTenant(slug, slug, ClientId, ownerEmail, temporaryPassword);
        }
        catch
        {
            // CreateRealm succeeded in this attempt. A conflict/failed create never reaches
            // this cleanup, so an existing tenant can never be deleted on a retry.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { await _admin.DeleteRealmAsync(slug, cleanup.Token); }
            catch { _logger.LogError("Failed to remove incomplete newly created realm {Realm}; explicit reconciliation required", slug); }
            throw;
        }
    }

    private async Task ConfigureRuntimeAccountAsync(string realm, string clientId, string secret, CancellationToken ct)
    {
        // Explicit platform provisioning owns this configuration. Employee runtime cannot perform it.
        await _admin.CreateClientAsync(realm, new { clientId, enabled = true, publicClient = false,
            serviceAccountsEnabled = true, standardFlowEnabled = false, directAccessGrantsEnabled = false,
            fullScopeAllowed = true, secret }, ct);
        var client = (await _admin.GetClientsAsync(realm, clientId, ct))[0];
        var account = await _admin.GetServiceAccountAsync(realm, client.GetProperty("id").GetString()!, ct);
        var management = (await _admin.GetClientsAsync(realm, "realm-management", ct))[0];
        var managementId = management.GetProperty("id").GetString()!;
        var roles = new List<object>();
        foreach (var name in new[] { "manage-users", "query-users", "view-users", "view-realm" })
            roles.Add(await _admin.GetClientRoleAsync(realm, managementId, name, ct));
        await _admin.AssignClientRolesAsync(realm, account.GetProperty("id").GetString()!, managementId, roles.ToArray(), ct);
        var profile = await _admin.GetUserProfileAsync(realm, ct);
        var fields = profile.EnumerateObject().ToDictionary(p => p.Name, p => (object)p.Value.Clone());
        var attributes = profile.GetProperty("attributes").EnumerateArray().Select(a => a.Clone()).ToList();
        attributes.Add(JsonSerializer.SerializeToElement(new { name = "cleaning_employee_id", displayName = "Employee ownership",
            multivalued = false, permissions = new { view = new[] { "admin" }, edit = new[] { "admin" } } }));
        fields["attributes"] = attributes;
        await _admin.UpdateUserProfileAsync(realm, fields, ct);
        var smtpSection = _configuration!.GetSection("EmployeeInvites:Smtp");
        var smtp = new Dictionary<string, string> { ["host"] = smtpSection["Host"]!, ["from"] = smtpSection["From"]!,
            ["port"] = smtpSection["Port"] ?? "587", ["auth"] = smtpSection.GetValue("Auth", true).ToString().ToLowerInvariant(),
            ["starttls"] = smtpSection.GetValue("StartTls", true).ToString().ToLowerInvariant(),
            ["ssl"] = smtpSection.GetValue("Ssl", false).ToString().ToLowerInvariant() };
        if (smtp["auth"].Equals("true", StringComparison.OrdinalIgnoreCase)) {
            smtp["user"] = smtpSection["User"] ?? "";
            smtp["password"] = smtpSection.GetValue<bool>("UseResendApiKey") ? _configuration["Resend:ApiKey"] ?? "" : smtpSection["Password"] ?? "";
            if (string.IsNullOrWhiteSpace(smtp["user"]) || string.IsNullOrWhiteSpace(smtp["password"]))
                throw new KeycloakException("Invitation SMTP authentication configuration is incomplete");
        }
        var current = await _admin.GetRealmAsync(realm, ct);
        var realmAttributes = current.TryGetProperty("attributes", out var existingAttributes) && existingAttributes.ValueKind == JsonValueKind.Object
            ? existingAttributes.EnumerateObject().ToDictionary(p => p.Name, p => (object)p.Value.Clone()) : new Dictionary<string, object>();
        realmAttributes["frontendUrl"] = _configuration["Auth:Keycloak:ServerUrl"]!.TrimEnd('/');
        await _admin.UpdateRealmAsync(realm, new { attributes = realmAttributes, smtpServer = smtp }, ct);
    }

    public Task<bool> RealmExistsAsync(string realm, CancellationToken ct = default) =>
        _admin.RealmExistsAsync(realm, ct);

    public Task SetRealmEnabledAsync(string realm, bool enabled, CancellationToken ct = default) =>
        _admin.SetRealmEnabledAsync(realm, enabled, ct);

    public static object AudienceMapper(string audience) => new
    {
        name = "cleaning-suite-api-audience", protocol = "openid-connect",
        protocolMapper = "oidc-audience-mapper", consentRequired = false,
        config = new Dictionary<string, string>
        {
            ["included.custom.audience"] = audience,
            ["access.token.claim"] = "true", ["id.token.claim"] = "false",
        },
    };

    private async Task WaitForRealmAsync(string realm, CancellationToken ct)
    {
        // Realm creation is eventually consistent; poll until the realm answers.
        for (var attempt = 0; attempt < 15; attempt++)
        {
            if (await _admin.RealmExistsAsync(realm, ct))
                return;
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }

        throw new KeycloakException($"Realm {realm} did not become ready in time");
    }

    private static string GenerateTemporaryPassword()
    {
        const string alphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var bytes = RandomNumberGenerator.GetBytes(16);
        var chars = new char[16];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = alphabet[bytes[i] % alphabet.Length];
        return new string(chars);
    }
}
