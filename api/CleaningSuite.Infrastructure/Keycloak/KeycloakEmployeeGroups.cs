using System.Text.Json;
using CleaningSuite.Application.Employees;

namespace CleaningSuite.Infrastructure.Keycloak;

/// <summary>Fixed setup-owned top-level groups; no request supplies group paths or IDs.</summary>
public sealed record KeycloakEmployeeGroups(string ManagedId, IReadOnlyDictionary<string, string> RoleIds)
{
    public const string ManagedPath = "/managed-users";
    public static readonly IReadOnlyDictionary<string, string> RolePaths = new Dictionary<string, string>
    { ["employee"] = "/app-employee", ["manager"] = "/app-manager", ["admin"] = "/app-admin" };

    public static async Task<KeycloakEmployeeGroups> ResolveAsync(KeycloakAdminClient client, string realm, CancellationToken ct = default)
    {
        async Task<string> Resolve(string path) {
            var group = await client.GetEmployeeGroupAsync(realm, path[1..], ct);
            if (group.ValueKind != JsonValueKind.Object || Text(group, "path") != path
                || Text(group, "name") != path[1..] || string.IsNullOrWhiteSpace(Text(group, "id"))
                || !string.IsNullOrEmpty(Text(group, "parentId")))
                throw new KeycloakException("Required fixed employee group is missing or invalid; verify explicit identity setup");
            return Text(group, "id");
        }
        var managed = await Resolve(ManagedPath);
        var roles = new Dictionary<string, string>();
        foreach (var role in RolePaths) roles.Add(role.Key, await Resolve(role.Value));
        if (roles.Values.Append(managed).Distinct(StringComparer.Ordinal).Count() != 4)
            throw new KeycloakException("Employee group identities are ambiguous");
        foreach (var entry in roles.Append(new KeyValuePair<string, string>("", managed))) {
            var allMappings = await client.GetGroupRoleMappingsAsync(realm, entry.Value, ct);
            if (allMappings.ValueKind != JsonValueKind.Object)
                throw new KeycloakException("Fixed employee group privileges could not be verified");
            if (allMappings.TryGetProperty("clientMappings", out var clients) && clients.ValueKind == JsonValueKind.Object && clients.EnumerateObject().Any())
                throw new KeycloakException("Fixed employee groups must not grant client administration roles");
            var mappings = await client.GetEffectiveGroupRealmRolesAsync(realm, entry.Value, ct);
            if (mappings.ValueKind != JsonValueKind.Array)
                throw new KeycloakException("Fixed employee group roles could not be verified");
            var actual = mappings.EnumerateArray().ToArray();
            if (actual.Length != (entry.Key == "" ? 0 : 1) || actual.Any(r => Text(r, "name") != entry.Key
                || r.TryGetProperty("composite", out var composite) && composite.ValueKind == JsonValueKind.True))
                throw new KeycloakException("Fixed employee group role mappings differ from explicit identity setup");
        }
        return new(managed, roles);
    }

    public async Task<JsonElement[]> ValidateUserAsync(KeycloakAdminClient client, string realm, Guid employeeId, string userId, JsonElement user, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(Text(user, "serviceAccountClientId")) || !user.TryGetProperty("attributes", out var attributes)
            || !attributes.TryGetProperty("cleaning_employee_id", out var owners) || owners.ValueKind != JsonValueKind.Array
            || owners.GetArrayLength() != 1 || employeeId == Guid.Empty || owners[0].GetString() != employeeId.ToString())
            throw new EmployeeIdentityConflictException("This login requires explicit employee ownership reconciliation.");
        var memberships = await client.GetUserGroupsAsync(realm, userId, ct);
        if (memberships.ValueKind != JsonValueKind.Array || memberships.GetArrayLength() > 100)
            throw new EmployeeIdentityConflictException("Login group membership could not be verified.");
        var result = memberships.EnumerateArray().ToArray();
        if (!result.Any(g => Text(g, "id") == ManagedId && Text(g, "path") == ManagedPath)
            || result.Any(g => Text(g, "id") != ManagedId && !RoleIds.Any(p => p.Value == Text(g, "id") && RolePaths[p.Key] == Text(g, "path"))))
            throw new EmployeeIdentityConflictException("This login is outside the configured employee groups. Explicit reconciliation is required.");
        var direct = await client.GetUserRealmRolesAsync(realm, userId, ct);
        if (direct.ValueKind != JsonValueKind.Array)
            throw new KeycloakException("Login role ownership could not be verified");
        if (direct.EnumerateArray().Any(r => RolePaths.ContainsKey(Text(r, "name"))))
            throw new EmployeeIdentityConflictException("Legacy direct employee roles require explicit administrator reconciliation.");
        var effective = await client.GetEffectiveUserRealmRolesAsync(realm, userId, ct);
        if (effective.ValueKind != JsonValueKind.Array)
            throw new KeycloakException("Effective login privileges could not be verified");
        if (effective.EnumerateArray().Any(r => Text(r, "name") is "realm-admin" or "admin" or "manager" or "employee"
            && !RoleIds.Any(p => p.Key == Text(r, "name") && result.Any(g => Text(g, "id") == p.Value))))
            throw new EmployeeIdentityConflictException("Unexpected inherited login privileges require administrator reconciliation.");
        return result;
    }
    internal static string Text(JsonElement value, string name) => value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() ?? "" : "";
}
