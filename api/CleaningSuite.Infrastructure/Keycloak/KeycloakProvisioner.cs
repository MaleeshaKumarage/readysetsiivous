using System.Security.Cryptography;
using CleaningSuite.Application.Tenants;
using Microsoft.Extensions.Logging;

namespace CleaningSuite.Infrastructure.Keycloak;

public class KeycloakProvisioner : IKeycloakProvisioner
{
    private const string ClientId = "cleaning-suite-web";
    private readonly KeycloakAdminClient _admin;
    private readonly ILogger<KeycloakProvisioner> _logger;

    public KeycloakProvisioner(KeycloakAdminClient admin, ILogger<KeycloakProvisioner> logger)
    {
        _admin = admin;
        _logger = logger;
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
        var temporaryPassword = GenerateTemporaryPassword();

        await _admin.CreateRealmAsync(slug, ct);
        await WaitForRealmAsync(slug, ct);

        await _admin.CreateRoleAsync(slug, "admin", ct);
        await _admin.CreateRoleAsync(slug, "employee", ct);

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
        }, ct);

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

    public Task<bool> RealmExistsAsync(string realm, CancellationToken ct = default) =>
        _admin.RealmExistsAsync(realm, ct);

    public Task SetRealmEnabledAsync(string realm, bool enabled, CancellationToken ct = default) =>
        _admin.SetRealmEnabledAsync(realm, enabled, ct);

    public async Task<InvitedEmployee> InviteEmployeeAsync(
        string realm,
        string email,
        string firstName,
        string lastName,
        string role,
        CancellationToken ct = default)
    {
        var temporaryPassword = GenerateTemporaryPassword();

        var userId = await _admin.GetUserIdByUsernameAsync(realm, email, ct);
        if (userId is null)
        {
            userId = await _admin.CreateUserAsync(realm, new
            {
                username = email,
                email,
                firstName,
                lastName,
                enabled = true,
                credentials = new[]
                {
                    new { type = "password", value = temporaryPassword, temporary = true },
                },
                requiredActions = new[] { "UPDATE_PASSWORD" },
            }, ct);
        }
        else
        {
            // Re-invite: reset to a fresh temporary password and re-arm the required action.
            await _admin.UpdateUserAsync(realm, userId, new
            {
                firstName,
                lastName,
                enabled = true,
                credentials = new[]
                {
                    new { type = "password", value = temporaryPassword, temporary = true },
                },
                requiredActions = new[] { "UPDATE_PASSWORD" },
            }, ct);
        }

        var roleRep = await _admin.GetRoleAsync(realm, role, ct);
        await _admin.AssignRealmRoleAsync(realm, userId, new object[] { roleRep }, ct);

        _logger.LogInformation("Invited employee {Email} in realm {Realm} as {Role}", email, realm, role);
        return new InvitedEmployee(userId, temporaryPassword);
    }

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
