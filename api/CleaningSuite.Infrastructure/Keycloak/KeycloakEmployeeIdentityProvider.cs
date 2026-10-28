using System.Net;
using System.Text.Json;
using CleaningSuite.Application.Employees;
using CleaningSuite.Domain.Employees;
using Microsoft.Extensions.Configuration;

namespace CleaningSuite.Infrastructure.Keycloak;
public sealed class KeycloakEmployeeIdentityProvider(TenantKeycloakClients identityClients, IConfiguration configuration, KeycloakInvitationPreflight? preflight = null) : IEmployeeIdentityProvider
{
    private const string ClientId = "cleaning-suite-web";
    private const string OwnerAttribute = "cleaning_employee_id";
    public async Task ValidateInvitationAsync(string tenantId, string redirectUri, string role, CancellationToken ct = default)
    {
        try {
            var realm = await identityClients.ForTenant(tenantId).GetRealmAsync(tenantId, ct);
            if (!Boolean(realm, "enabled")) throw new EmployeeInviteConfigurationException("The tenant's login realm is disabled.");
            if (role == Employee.RoleManager) await identityClients.ForTenant(tenantId).GetRoleAsync(tenantId, Employee.RoleManager, ct);
            foreach (var requiredRole in new[] { Employee.RoleAdmin, Employee.RoleEmployee }) await identityClients.ForTenant(tenantId).GetRoleAsync(tenantId, requiredRole, ct);
            await (preflight ?? new KeycloakInvitationPreflight()).ValidateAsync(configuration, tenantId, redirectUri, ct);
            var attributes = realm.TryGetProperty("attributes", out var existing) && existing.ValueKind == JsonValueKind.Object
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(existing.GetRawText())! : new();
            var publicUrl = configuration["Auth:Keycloak:ServerUrl"]!.TrimEnd('/');
            if (attributes.TryGetValue("frontendUrl", out var current) && !string.IsNullOrWhiteSpace(current) && current.TrimEnd('/') != publicUrl)
                throw new EmployeeInviteConfigurationException("The realm frontend URL differs from the configured public Keycloak URL.");
            if (!attributes.TryGetValue("frontendUrl", out var configuredFrontend) || string.IsNullOrWhiteSpace(configuredFrontend))
                throw new EmployeeInviteConfigurationException("Configure the realm frontend URL using the explicit identity setup procedure.");
            var smtp = realm.TryGetProperty("smtpServer", out var mail) && mail.ValueKind == JsonValueKind.Object
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(mail.GetRawText())! : new();
            if (!smtp.TryGetValue("host", out var host) || string.IsNullOrWhiteSpace(host))
                throw new EmployeeInviteConfigurationException("Configure realm SMTP using the explicit identity setup procedure.");
            if (!smtp.TryGetValue("from", out var from) || string.IsNullOrWhiteSpace(from))
                throw new EmployeeInviteConfigurationException("Keycloak invitation mail requires an SMTP sender address.");
            await EnsureOwnershipAttributeAsync(tenantId, ct);
            await KeycloakEmployeeGroups.ResolveAsync(identityClients.ForTenant(tenantId), tenantId, ct);
        } catch (KeycloakException ex) { throw new EmployeeInviteConfigurationException($"Keycloak invitation configuration could not be verified ({(int?)ex.StatusCode}). Check realm roles, client and mail settings."); }
        catch (HttpRequestException) { throw new EmployeeInviteConfigurationException("Keycloak invitation configuration could not be verified. Check realm roles, client and mail settings."); }
    }
    public async Task<EmployeeIdentity> EnsureAsync(string tenantId, Employee employee, CancellationToken ct = default)
    {
        if (employee.Role is not (Employee.RoleAdmin or Employee.RoleEmployee or Employee.RoleManager)) throw new EmployeeIdentityConflictException("Unsupported employee role.");
        try {
            var groups = await KeycloakEmployeeGroups.ResolveAsync(identityClients.ForTenant(tenantId), tenantId, ct);
            JsonElement? user;
            if (!string.IsNullOrWhiteSpace(employee.KeycloakUserId)) {
                user = await identityClients.ForTenant(tenantId).GetUserAsync(tenantId, employee.KeycloakUserId, ct);
                if (user is null) throw new EmployeeIdentityConflictException("The linked login no longer exists. Reconcile this employee's identity before reinviting.");
            } else {
                user = await FindUniqueAsync(tenantId, employee.Email, ct);
                if (user is null) {
                    try {
                        var id = await identityClients.ForTenant(tenantId).CreateUserAsync(tenantId, new { username = employee.Email.Trim(), email = employee.Email.Trim(),
                            firstName = employee.FirstName, lastName = employee.LastName, enabled = true, emailVerified = false,
                            attributes = new Dictionary<string, string[]> { [OwnerAttribute] = new[] { employee.Id.ToString() } },
                            requiredActions = new[] { "VERIFY_EMAIL", "UPDATE_PASSWORD" }, groups = new[] { KeycloakEmployeeGroups.ManagedPath } }, ct);
                        user = await identityClients.ForTenant(tenantId).GetUserAsync(tenantId, id, ct);
                    } catch (KeycloakException ex) when (ex.StatusCode == HttpStatusCode.Conflict) {
                        user = await FindUniqueAsync(tenantId, employee.Email, ct);
                    }
                    if (user is null) throw new EmployeeIdentityConflictException("Account creation could not be reconciled. Retry or contact an administrator.");
                }
            }
            var account = user.Value;
            if (!String(account, "email").Equals(employee.Email.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new EmployeeIdentityConflictException("The linked login email differs from this employee. Reconcile it before sending an invitation.");
            if (!Boolean(account, "enabled")) throw new EmployeeIdentityConflictException("The login account is disabled. Reactivate it explicitly before inviting.");
            var attributes = account.TryGetProperty("attributes", out var attrs) && attrs.ValueKind == JsonValueKind.Object
                ? JsonSerializer.Deserialize<Dictionary<string, string[]>>(attrs.GetRawText())! : new();
            var owners = attributes.GetValueOrDefault(OwnerAttribute) ?? Array.Empty<string>();
            if (owners.Length > 1) throw new EmployeeIdentityConflictException("The login has ambiguous employee ownership. Administrator reconciliation is required.");
            var owner = owners.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(owner) && owner != employee.Id.ToString())
                throw new EmployeeIdentityConflictException("This login belongs to another employee record.");
            var idValue = String(account, "id");
            if (owner != employee.Id.ToString())
                throw new EmployeeIdentityConflictException("Existing login accounts require explicit administrator ownership reconciliation.");
            await groups.ValidateUserAsync(identityClients.ForTenant(tenantId), tenantId, employee.Id, idValue, account, ct);
            attributes[OwnerAttribute] = new[] { employee.Id.ToString() };
            // Intentionally no credentials or requiredActions update: retries preserve passwords/setup progress.
            await identityClients.ForTenant(tenantId).UpdateUserAsync(tenantId, idValue, new { firstName = employee.FirstName, lastName = employee.LastName, attributes }, ct);
            var confirmed = await identityClients.ForTenant(tenantId).GetUserAsync(tenantId, idValue, ct);
            if (confirmed is null || !confirmed.Value.TryGetProperty("attributes", out var persisted)
                || !persisted.TryGetProperty(OwnerAttribute, out var persistedOwner)
                || persistedOwner.ValueKind != JsonValueKind.Array || persistedOwner.GetArrayLength() != 1
                || persistedOwner[0].GetString() != employee.Id.ToString())
                throw new EmployeeIdentityConflictException("Keycloak did not preserve employee ownership. Check its user profile configuration before retrying.");
            return new(idValue);
        } catch (Exception ex) when (ex is KeycloakException or HttpRequestException || ex is TaskCanceledException && !ct.IsCancellationRequested) { throw new EmployeeIdentityProviderException("Could not prepare the login account. The invitation was not sent; retry after checking Keycloak.", ex); }
    }
    public async Task AssignRoleAsync(string tenantId, Guid employeeId, string userId, string role, CancellationToken ct = default) {
        try { await SetRoleAsync(tenantId, employeeId, userId, role, ct); }
        catch (Exception ex) when (ex is KeycloakException or HttpRequestException || ex is TaskCanceledException && !ct.IsCancellationRequested) {
            throw new EmployeeIdentityProviderException("The employee is linked, but its login role could not be synchronized. Retry the invitation.", ex);
        }
    }
    public async Task SendSetupEmailAsync(string tenantId, Guid employeeId, string userId, string redirectUri, int lifespanSeconds, CancellationToken ct = default)
    {
        try {
            var user = await identityClients.ForTenant(tenantId).GetUserAsync(tenantId, userId, ct) ?? throw new EmployeeIdentityConflictException("The linked login no longer exists.");
            var groups = await KeycloakEmployeeGroups.ResolveAsync(identityClients.ForTenant(tenantId), tenantId, ct);
            await groups.ValidateUserAsync(identityClients.ForTenant(tenantId), tenantId, employeeId, userId, user, ct);
            var actions = new List<string>();
            if (!Boolean(user, "emailVerified")) actions.Add("VERIFY_EMAIL");
            if (user.TryGetProperty("requiredActions", out var required) && required.EnumerateArray().Any(a => a.GetString() == "UPDATE_PASSWORD")) actions.Add("UPDATE_PASSWORD");
            await identityClients.ForTenant(tenantId).SendExecuteActionsEmailAsync(tenantId, userId, ClientId, redirectUri, lifespanSeconds, actions.ToArray(), ct);
        } catch (Exception ex) when (ex is KeycloakException or HttpRequestException || ex is TaskCanceledException && !ct.IsCancellationRequested) { throw new EmployeeIdentityProviderException("The account is linked, but its setup email could not be sent. Retry the invitation after checking mail delivery.", ex); }
    }
    public async Task SynchronizeAccessAsync(string tenantId, Guid employeeId, string userId, string role, bool enabled, CancellationToken ct = default)
    {
        try {
            _ = await identityClients.ForTenant(tenantId).GetUserAsync(tenantId, userId, ct) ?? throw new EmployeeIdentityConflictException("The linked login no longer exists.");
            await SetRoleAsync(tenantId, employeeId, userId, role, ct);
            await identityClients.ForTenant(tenantId).UpdateUserAsync(tenantId, userId, new { enabled }, ct);
            // Drop sessions again after changing enablement, including deactivation.
            await identityClients.ForTenant(tenantId).LogoutUserAsync(tenantId, userId, ct);
        } catch (Exception ex) when (ex is KeycloakException or HttpRequestException || ex is TaskCanceledException && !ct.IsCancellationRequested) { throw new EmployeeIdentityProviderException("Employee access was saved locally, but login synchronization failed. Retry this update.", ex); }
    }
    private async Task EnsureOwnershipAttributeAsync(string tenantId, CancellationToken ct) {
        var profile = await identityClients.ForTenant(tenantId).GetUserProfileAsync(tenantId, ct);
        var attributes = profile.TryGetProperty("attributes", out var defined) ? defined.EnumerateArray().Select(x => x.Clone()).ToList() : new List<JsonElement>();
        var owner = attributes.FirstOrDefault(a => String(a, "name") == OwnerAttribute);
        if (owner.ValueKind != JsonValueKind.Undefined) {
            if (!owner.TryGetProperty("permissions", out var permissions)
                || !permissions.TryGetProperty("edit", out var edit) || edit.ValueKind != JsonValueKind.Array
                || edit.GetArrayLength() != 1 || edit[0].GetString() != "admin"
                || !permissions.TryGetProperty("view", out var view) || view.ValueKind != JsonValueKind.Array
                || view.GetArrayLength() != 1 || view[0].GetString() != "admin")
                throw new EmployeeInviteConfigurationException("The employee ownership attribute must be viewable/editable only by Keycloak administrators.");
            return;
        }
        throw new EmployeeInviteConfigurationException("Configure the administrator-only employee ownership attribute using the explicit identity setup procedure.");
    }
    private async Task<JsonElement?> FindUniqueAsync(string tenantId, string email, CancellationToken ct) {
        var users = await identityClients.ForTenant(tenantId).FindUsersByEmailAsync(tenantId, email.Trim(), ct);
        var matches = users.EnumerateArray().Where(u => String(u, "email").Equals(email.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count > 1) throw new EmployeeIdentityConflictException("More than one login account has this email. Administrator reconciliation is required.");
        return matches.Count == 0 ? null : matches[0];
    }
    private async Task SetRoleAsync(string tenantId, Guid employeeId, string userId, string role, CancellationToken ct) {
        if (role is not (Employee.RoleAdmin or Employee.RoleEmployee or Employee.RoleManager)) throw new EmployeeIdentityConflictException("Unsupported employee role.");
        var client = identityClients.ForTenant(tenantId);
        var groups = await KeycloakEmployeeGroups.ResolveAsync(client, tenantId, ct);
        var user = await client.GetUserAsync(tenantId, userId, ct) ?? throw new EmployeeIdentityConflictException("The linked login no longer exists.");
        var memberships = await groups.ValidateUserAsync(client, tenantId, employeeId, userId, user, ct);
        var mutationAttempted = false;
        try {
            foreach (var previous in groups.RoleIds.Where(p => p.Key != role && memberships.Any(g => String(g, "id") == p.Value)))
            {
                mutationAttempted = true;
                await client.RemoveUserFromGroupAsync(tenantId, userId, previous.Value, ct);
            }
            if (!memberships.Any(g => String(g, "id") == groups.RoleIds[role]))
            {
                mutationAttempted = true;
                await client.AddUserToGroupAsync(tenantId, userId, groups.RoleIds[role], ct);
            }
            var effective = await client.GetEffectiveUserRealmRolesAsync(tenantId, userId, ct);
            var actual = effective.EnumerateArray().Where(r => KeycloakEmployeeGroups.RolePaths.ContainsKey(String(r, "name"))).Select(r => String(r, "name")).ToArray();
            if (actual.Length != 1 || actual[0] != role)
                throw new EmployeeIdentityConflictException("Login role membership did not yield the intended access. Retry after verifying explicit identity setup.");
            // A verified no-op must preserve the invitation already delivered:
            // extension logout also revokes its ticket. Actual access changes
            // still invalidate sessions, including the partial-failure path.
            if (mutationAttempted) await client.LogoutUserAsync(tenantId, userId, ct);
        } catch {
            if (mutationAttempted) {
                // Revocation must survive caller cancellation and must not mask the original failure.
                using var revocation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try { await client.LogoutUserAsync(tenantId, userId, revocation.Token); }
                catch (Exception error) when (error is KeycloakException or HttpRequestException or OperationCanceledException) { }
            }
            throw;
        }

    }
    private static string String(JsonElement element, string name) => element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() ?? "" : "";
    private static bool Boolean(JsonElement element, string name) => element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.True;
}
