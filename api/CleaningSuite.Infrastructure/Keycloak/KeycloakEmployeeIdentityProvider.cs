using System.Net;
using System.Text.Json;
using CleaningSuite.Application.Employees;
using CleaningSuite.Domain.Employees;
using Microsoft.Extensions.Configuration;

namespace CleaningSuite.Infrastructure.Keycloak;
public sealed class KeycloakEmployeeIdentityProvider(KeycloakAdminClient admin, IConfiguration configuration) : IEmployeeIdentityProvider
{
    private const string ClientId = "cleaning-suite-web";
    private const string OwnerAttribute = "cleaning_employee_id";
    public async Task ValidateInvitationAsync(string tenantId, string redirectUri, string role, CancellationToken ct = default)
    {
        try {
            var realm = await admin.GetRealmAsync(tenantId, ct);
            if (!Boolean(realm, "enabled")) throw new EmployeeInviteConfigurationException("The tenant's login realm is disabled.");
            if (role == Employee.RoleManager) await EnsureManagerRoleAsync(tenantId, ct);
            foreach (var requiredRole in new[] { Employee.RoleAdmin, Employee.RoleEmployee }) await admin.GetRoleAsync(tenantId, requiredRole, ct);
            var clients = await admin.GetClientsAsync(tenantId, ClientId, ct);
            var client = clients.EnumerateArray().FirstOrDefault(c => String(c, "clientId") == ClientId);
            if (client.ValueKind == JsonValueKind.Undefined || !Boolean(client, "enabled")
                || !client.TryGetProperty("redirectUris", out var redirects)
                || !redirects.EnumerateArray().Any(uri => RedirectAllowed(uri.GetString(), redirectUri)))
                throw new EmployeeInviteConfigurationException("The account landing URL is not registered for the Keycloak web client.");
            var attributes = realm.TryGetProperty("attributes", out var existing) && existing.ValueKind == JsonValueKind.Object
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(existing.GetRawText())! : new();
            var publicUrl = configuration["Auth:Keycloak:ServerUrl"]!.TrimEnd('/');
            if (attributes.TryGetValue("frontendUrl", out var current) && !string.IsNullOrWhiteSpace(current) && current.TrimEnd('/') != publicUrl)
                throw new EmployeeInviteConfigurationException("The realm frontend URL differs from the configured public Keycloak URL.");
            var updates = new Dictionary<string, object>();
            if (!attributes.TryGetValue("frontendUrl", out var configuredFrontend) || string.IsNullOrWhiteSpace(configuredFrontend)) { attributes["frontendUrl"] = publicUrl; updates["attributes"] = attributes; }
            var smtp = realm.TryGetProperty("smtpServer", out var mail) && mail.ValueKind == JsonValueKind.Object
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(mail.GetRawText())! : new();
            if (!smtp.TryGetValue("host", out var host) || string.IsNullOrWhiteSpace(host)) { smtp = ConfiguredSmtp(); updates["smtpServer"] = smtp; }
            if (!smtp.TryGetValue("from", out var from) || string.IsNullOrWhiteSpace(from))
                throw new EmployeeInviteConfigurationException("Keycloak invitation mail requires an SMTP sender address.");
            await EnsureOwnershipAttributeAsync(tenantId, ct);
            if (updates.Count > 0) await admin.UpdateRealmAsync(tenantId, updates, ct);
        } catch (KeycloakException ex) { throw new EmployeeInviteConfigurationException($"Keycloak invitation configuration could not be verified ({(int?)ex.StatusCode}). Check realm roles, client and mail settings."); }
        catch (HttpRequestException) { throw new EmployeeInviteConfigurationException("Keycloak invitation configuration could not be verified. Check realm roles, client and mail settings."); }
    }
    public async Task<EmployeeIdentity> EnsureAsync(string tenantId, Employee employee, CancellationToken ct = default)
    {
        if (employee.Role is not (Employee.RoleAdmin or Employee.RoleEmployee or Employee.RoleManager)) throw new EmployeeIdentityConflictException("Unsupported employee role.");
        try {
            JsonElement? user;
            if (!string.IsNullOrWhiteSpace(employee.KeycloakUserId)) {
                user = await admin.GetUserAsync(tenantId, employee.KeycloakUserId, ct);
                if (user is null) throw new EmployeeIdentityConflictException("The linked login no longer exists. Reconcile this employee's identity before reinviting.");
            } else {
                user = await FindUniqueAsync(tenantId, employee.Email, ct);
                if (user is null) {
                    try {
                        var id = await admin.CreateUserAsync(tenantId, new { username = employee.Email.Trim(), email = employee.Email.Trim(),
                            firstName = employee.FirstName, lastName = employee.LastName, enabled = true, emailVerified = false,
                            attributes = new Dictionary<string, string[]> { [OwnerAttribute] = new[] { employee.Id.ToString() } },
                            requiredActions = new[] { "VERIFY_EMAIL", "UPDATE_PASSWORD" } }, ct);
                        user = await admin.GetUserAsync(tenantId, id, ct);
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
            var roles = await admin.GetUserRealmRolesAsync(tenantId, idValue, ct);
            if (string.IsNullOrWhiteSpace(owner) && string.IsNullOrWhiteSpace(employee.KeycloakUserId)) {
                if (!Boolean(account, "emailVerified")) throw new EmployeeIdentityConflictException("An existing unverified account uses this email. Administrator reconciliation is required.");
                if (roles.EnumerateArray().Any(r => (String(r, "name") is Employee.RoleAdmin or Employee.RoleEmployee or Employee.RoleManager) && String(r, "name") != employee.Role))
                    throw new EmployeeIdentityConflictException("An existing account has a conflicting role. Administrator reconciliation is required.");
            }
            attributes[OwnerAttribute] = new[] { employee.Id.ToString() };
            // Intentionally no credentials or requiredActions update: retries preserve passwords/setup progress.
            await admin.UpdateUserAsync(tenantId, idValue, new { firstName = employee.FirstName, lastName = employee.LastName, attributes }, ct);
            var confirmed = await admin.GetUserAsync(tenantId, idValue, ct);
            if (confirmed is null || !confirmed.Value.TryGetProperty("attributes", out var persisted)
                || !persisted.TryGetProperty(OwnerAttribute, out var persistedOwner)
                || persistedOwner.ValueKind != JsonValueKind.Array || persistedOwner.GetArrayLength() != 1
                || persistedOwner[0].GetString() != employee.Id.ToString())
                throw new EmployeeIdentityConflictException("Keycloak did not preserve employee ownership. Check its user profile configuration before retrying.");
            return new(idValue);
        } catch (Exception ex) when (ex is KeycloakException or HttpRequestException || ex is TaskCanceledException && !ct.IsCancellationRequested) { throw new EmployeeIdentityProviderException("Could not prepare the login account. The invitation was not sent; retry after checking Keycloak.", ex); }
    }
    public async Task AssignRoleAsync(string tenantId, string userId, string role, CancellationToken ct = default) {
        try { await SetRoleAsync(tenantId, userId, role, ct); }
        catch (Exception ex) when (ex is KeycloakException or HttpRequestException || ex is TaskCanceledException && !ct.IsCancellationRequested) {
            throw new EmployeeIdentityProviderException("The employee is linked, but its login role could not be synchronized. Retry the invitation.", ex);
        }
    }
    public async Task SendSetupEmailAsync(string tenantId, string userId, string redirectUri, int lifespanSeconds, CancellationToken ct = default)
    {
        try {
            var user = await admin.GetUserAsync(tenantId, userId, ct) ?? throw new EmployeeIdentityConflictException("The linked login no longer exists.");
            var actions = new List<string>();
            if (!Boolean(user, "emailVerified")) actions.Add("VERIFY_EMAIL");
            if (user.TryGetProperty("requiredActions", out var required) && required.EnumerateArray().Any(a => a.GetString() == "UPDATE_PASSWORD")) actions.Add("UPDATE_PASSWORD");
            await admin.SendExecuteActionsEmailAsync(tenantId, userId, ClientId, redirectUri, lifespanSeconds, actions.ToArray(), ct);
        } catch (Exception ex) when (ex is KeycloakException or HttpRequestException || ex is TaskCanceledException && !ct.IsCancellationRequested) { throw new EmployeeIdentityProviderException("The account is linked, but its setup email could not be sent. Retry the invitation after checking mail delivery.", ex); }
    }
    public async Task SynchronizeAccessAsync(string tenantId, string userId, string role, bool enabled, CancellationToken ct = default)
    {
        try {
            _ = await admin.GetUserAsync(tenantId, userId, ct) ?? throw new EmployeeIdentityConflictException("The linked login no longer exists.");
            await SetRoleAsync(tenantId, userId, role, ct);
            await admin.UpdateUserAsync(tenantId, userId, new { enabled }, ct);
            // Drop old sessions for both role changes and deactivation. Local API guards also reject old tokens.
            await admin.LogoutUserAsync(tenantId, userId, ct);
        } catch (Exception ex) when (ex is KeycloakException or HttpRequestException || ex is TaskCanceledException && !ct.IsCancellationRequested) { throw new EmployeeIdentityProviderException("Employee access was saved locally, but login synchronization failed. Retry this update.", ex); }
    }
    private async Task EnsureOwnershipAttributeAsync(string tenantId, CancellationToken ct) {
        var profile = await admin.GetUserProfileAsync(tenantId, ct);
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
        attributes.Add(JsonSerializer.SerializeToElement(new { name = OwnerAttribute, displayName = "Employee ownership",
            multivalued = false, permissions = new { view = new[] { "admin" }, edit = new[] { "admin" } } }));
        var updated = profile.EnumerateObject().ToDictionary(p => p.Name, p => (object)p.Value.Clone());
        updated["attributes"] = attributes;
        await admin.UpdateUserProfileAsync(tenantId, updated, ct);
    }
    private async Task<JsonElement?> FindUniqueAsync(string tenantId, string email, CancellationToken ct) {
        var users = await admin.FindUsersByEmailAsync(tenantId, email.Trim(), ct);
        var matches = users.EnumerateArray().Where(u => String(u, "email").Equals(email.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count > 1) throw new EmployeeIdentityConflictException("More than one login account has this email. Administrator reconciliation is required.");
        return matches.Count == 0 ? null : matches[0];
    }
    private async Task SetRoleAsync(string tenantId, string userId, string role, CancellationToken ct) {
        if (role is not (Employee.RoleAdmin or Employee.RoleEmployee or Employee.RoleManager)) throw new EmployeeIdentityConflictException("Unsupported employee role.");
        if (role == Employee.RoleManager) await EnsureManagerRoleAsync(tenantId, ct);
        var wanted = await admin.GetRoleAsync(tenantId, role, ct);
        var existing = await admin.GetUserRealmRolesAsync(tenantId, userId, ct);
        var unwanted = existing.EnumerateArray().Where(r => (String(r, "name") is Employee.RoleAdmin or Employee.RoleEmployee or Employee.RoleManager) && String(r, "name") != role).Select(r => (object)r).ToArray();
        // Remove elevated stale role before assigning the intended role; failures cannot leave both.
        if (unwanted.Length > 0) await admin.RemoveRealmRolesAsync(tenantId, userId, unwanted, ct);
        if (!existing.EnumerateArray().Any(r => String(r, "name") == role)) await admin.AssignRealmRoleAsync(tenantId, userId, new object[] { wanted }, ct);
    }
    private async Task EnsureManagerRoleAsync(string tenantId, CancellationToken ct) {
        try { await admin.CreateRoleAsync(tenantId, Employee.RoleManager, ct); }
        catch (KeycloakException e) when (e.StatusCode == System.Net.HttpStatusCode.Conflict) {
            // Realm imports, provisioning or another admin request may already have created it.
            _ = await admin.GetRoleAsync(tenantId, Employee.RoleManager, ct);
        }
    }
    private Dictionary<string, string> ConfiguredSmtp() {
        var section = configuration.GetSection("EmployeeInvites:Smtp");
        var host = section["Host"];
        var from = section["From"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from))
            throw new EmployeeInviteConfigurationException("Configure SMTP in the tenant's Keycloak realm or EmployeeInvites:Smtp before sending invitations.");
        var auth = section.GetValue("Auth", true);
        var password = section["Password"];
        if (section.GetValue("UseResendApiKey", false)) password = configuration["Resend:ApiKey"];
        if (auth && (string.IsNullOrWhiteSpace(section["User"]) || string.IsNullOrWhiteSpace(password)))
            throw new EmployeeInviteConfigurationException("Invitation SMTP authentication credentials are missing.");
        var smtp = new Dictionary<string, string> { ["host"] = host, ["from"] = from, ["port"] = section["Port"] ?? "587",
            ["auth"] = auth.ToString().ToLowerInvariant(), ["starttls"] = section.GetValue("StartTls", true).ToString().ToLowerInvariant(),
            ["ssl"] = section.GetValue("Ssl", false).ToString().ToLowerInvariant() };
        if (auth) { smtp["user"] = section["User"]!; smtp["password"] = password!; }
        return smtp;
    }
    private static bool RedirectAllowed(string? registered, string actual) => registered is not null &&
        (registered == actual || (registered.EndsWith("/*", StringComparison.Ordinal) && actual.StartsWith(registered[..^1], StringComparison.Ordinal)));
    private static string String(JsonElement element, string name) => element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() ?? "" : "";
    private static bool Boolean(JsonElement element, string name) => element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.True;
}
