namespace CleaningSuite.Application.Tenants;

public record ProvisionedTenant(
    string Slug,
    string Realm,
    string ClientId,
    string OwnerEmail,
    string TemporaryPassword);

public interface IKeycloakProvisioner
{
    /// <summary>
    /// Creates a realm (== tenant slug), admin/employee roles, the public SPA
    /// client and the owner admin user with a temporary password.
    /// Realm creation is eventually consistent; polls until the realm answers.
    /// </summary>
    Task<ProvisionedTenant> ProvisionAsync(
        string slug,
        string companyName,
        string ownerEmail,
        string ownerFirstName,
        string ownerLastName,
        string[] redirectUris,
        CancellationToken ct = default);

    Task<bool> RealmExistsAsync(string realm, CancellationToken ct = default);

    /// <summary>Creates a realm user with a temporary password and assigns the given realm role.</summary>
    Task<InvitedEmployee> InviteEmployeeAsync(
        string realm,
        string email,
        string firstName,
        string lastName,
        string role,
        CancellationToken ct = default);

    Task SetRealmEnabledAsync(string realm, bool enabled, CancellationToken ct = default);
}

public record InvitedEmployee(string KeycloakUserId, string TemporaryPassword);
