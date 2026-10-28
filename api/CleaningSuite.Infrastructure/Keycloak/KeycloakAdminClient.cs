using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace CleaningSuite.Infrastructure.Keycloak;

/// <summary>
/// Explicit platform Admin REST client or tenant employee-extension client.
/// Runtime construction never accepts bootstrap passwords or falls back to Admin REST.
/// </summary>
public class KeycloakAdminClient
{
    public const string RuntimeAudience = "cleaning-identity-runtime";
    public const string RuntimeClientAttribute = "cleaning_identity_runtime_client_id";
    private readonly HttpClient _http;
    private readonly string _serverUrl;
    private readonly string _provisionerRealm;
    private readonly string _provisionerClientId;
    private readonly string _provisionerClientSecret;
    private readonly string? _allowedRealm;

    private string? _cachedToken;
    private DateTime _tokenRefreshUtc = DateTime.MinValue;

    public KeycloakAdminClient(
        HttpClient http,
        string serverUrl,
        string provisionerRealm,
        string provisionerClientId,
        string provisionerClientSecret,
        string? allowedRealm = null)
    {
        _http = http;
        _serverUrl = serverUrl.TrimEnd('/');
        _provisionerRealm = provisionerRealm;
        _provisionerClientId = provisionerClientId;
        _provisionerClientSecret = provisionerClientSecret;
        _allowedRealm = allowedRealm;
    }

    public virtual async Task<string> GetAdminTokenAsync(CancellationToken ct = default)
    {
        if (_cachedToken is not null && DateTime.UtcNow < _tokenRefreshUtc)
            return _cachedToken;

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_serverUrl}/realms/{_provisionerRealm}/protocol/openid-connect/token");

        if (!string.IsNullOrEmpty(_provisionerClientSecret))
        {
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _provisionerClientId,
                ["client_secret"] = _provisionerClientSecret,
            });
        }
        else
        {
            throw new InvalidOperationException("Keycloak service-account credentials not configured");
        }

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new KeycloakException($"Admin token failed: {(int)response.StatusCode}");

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var accessToken = payload.GetProperty("access_token").GetString();
        if (string.IsNullOrWhiteSpace(accessToken)) throw new KeycloakException("access_token missing");
        _cachedToken = accessToken;
        var expiresIn = payload.GetProperty("expires_in").GetInt32();
        // A fixed five-minute skew made five-minute (and shorter) tokens miss
        // the cache on every request. Keep a proportional, bounded safety margin.
        var lifetime = Math.Max(0, expiresIn);
        _tokenRefreshUtc = DateTime.UtcNow.AddSeconds(lifetime - Math.Min(30, lifetime * 0.1));

        return _cachedToken;
    }

    public async Task<bool> RealmExistsAsync(string realm, CancellationToken ct = default)
    {
        var response = await SendAsync(HttpMethod.Get, $"admin/realms/{realm}", ct, expectedNotFoundOk: true);
        return response.IsSuccessStatusCode;
    }

    // Prove the exact delivered credential against its realm token endpoint, without
    // using the platform token cache or reading a masked client-secret representation.
    public async Task VerifyServiceAccountAsync(string realm, string clientId, string secret, CancellationToken ct = default)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(realm, "^[a-z0-9][a-z0-9-]{2,62}$")
            || (_allowedRealm is not null && realm != _allowedRealm))
            throw new KeycloakException("Invalid service-account verification realm");
        var verifier = new KeycloakAdminClient(_http, _serverUrl, realm, clientId, secret, realm);
        await verifier.GetAdminTokenAsync(ct);
    }

    // New realm readiness includes the actual server-side employee boundary;
    // successful client-credentials exchange alone also works on stock Keycloak.
    public async Task VerifyEmployeeExtensionAsync(string realm, string clientId, string secret, CancellationToken ct = default)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(realm, "^[a-z0-9][a-z0-9-]{2,62}$")
            || (_allowedRealm is not null && realm != _allowedRealm))
            throw new KeycloakException("Invalid employee-extension verification realm");
        var verifier = new KeycloakAdminClient(_http, _serverUrl, realm, clientId, secret, realm);
        var representation = await verifier.GetRealmAsync(realm, ct);
        if (representation.ValueKind != JsonValueKind.Object || !representation.TryGetProperty("enabled", out var enabled)
            || enabled.ValueKind != JsonValueKind.True)
            throw new KeycloakException("Employee identity extension readiness could not be verified");
    }

    public Task CreateRealmAsync(string realm, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "admin/realms", ct, new
        {
            realm,
            enabled = true,
            registrationAllowed = false,
            loginWithEmailAllowed = true,
        });

    public Task CreateRoleAsync(string realm, string roleName, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"admin/realms/{realm}/roles", ct, new { name = roleName });

    public Task DeleteRealmAsync(string realm, CancellationToken ct = default) =>
        SendWithoutResultAsync(HttpMethod.Delete, $"admin/realms/{Uri.EscapeDataString(realm)}", ct);

    public Task CreateClientAsync(string realm, object client, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"admin/realms/{realm}/clients", ct, client);

    public Task<string> CreateUserAsync(string realm, object user, CancellationToken ct = default) =>
        SendForLocationAsync(HttpMethod.Post, $"admin/realms/{realm}/users", ct, user);

    public async Task<string?> GetUserIdByUsernameAsync(string realm, string username, CancellationToken ct = default)
    {
        var response = await SendAsync(
            HttpMethod.Get,
            $"admin/realms/{realm}/users?username={Uri.EscapeDataString(username)}&exact=true",
            ct, expectedNotFoundOk: true);
        if (!response.IsSuccessStatusCode)
            return null;

        var users = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return users.ValueKind == JsonValueKind.Array && users.GetArrayLength() > 0
            ? users[0].GetProperty("id").GetString()
            : null;
    }

    public async Task UpdateUserAsync(string realm, string userId, object changes, CancellationToken ct = default)
    {
        if (_allowedRealm is not null) {
            // The extension accepts a narrow patch, not an Admin REST representation.
            // In particular, do not echo id/email/verification/requiredActions fields
            // from reads into a write that must never control those fields.
            await SendWithoutResultAsync(HttpMethod.Put, $"admin/realms/{Uri.EscapeDataString(realm)}/users/{Uri.EscapeDataString(userId)}", ct, changes);
            return;
        }
        // Keycloak user-profile updates can clear omitted fields (including email).
        // Merge changes onto the current representation while leaving credentials and
        // requiredActions untouched unless this operation explicitly supplies them.
        var current = await GetUserAsync(realm, userId, ct) ?? throw new KeycloakException("Login account no longer exists", System.Net.HttpStatusCode.NotFound);
        var merged = current.EnumerateObject().Where(p => p.Name is not ("credentials" or "requiredActions"))
            .ToDictionary(p => p.Name, p => (object)p.Value.Clone());
        foreach (var field in JsonSerializer.SerializeToElement(changes).EnumerateObject()) merged[field.Name] = field.Value.Clone();
        await SendWithoutResultAsync(HttpMethod.Put, $"admin/realms/{Uri.EscapeDataString(realm)}/users/{Uri.EscapeDataString(userId)}", ct, merged);
    }

    public Task SetRealmEnabledAsync(string realm, bool enabled, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, $"admin/realms/{realm}", ct, new { enabled });

    public async Task<JsonElement> GetRoleAsync(string realm, string roleName, CancellationToken ct = default)
    {
        var response = await SendAsync(HttpMethod.Get, $"admin/realms/{realm}/roles/{roleName}", ct, expectedNotFoundOk: true);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    public Task AssignRealmRoleAsync(string realm, string userId, object[] roles, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"admin/realms/{realm}/users/{userId}/role-mappings/realm", ct, roles);

    public Task<JsonElement> GetUserProfileAsync(string realm, CancellationToken ct = default) => GetJsonAsync($"admin/realms/{Uri.EscapeDataString(realm)}/users/profile", ct);
    public Task UpdateUserProfileAsync(string realm, object profile, CancellationToken ct = default) => SendWithoutResultAsync(HttpMethod.Put, $"admin/realms/{Uri.EscapeDataString(realm)}/users/profile", ct, profile);
    public Task<JsonElement> GetRealmAsync(string realm, CancellationToken ct = default) => GetJsonAsync($"admin/realms/{Uri.EscapeDataString(realm)}", ct);
    public Task UpdateRealmAsync(string realm, object changes, CancellationToken ct = default) => SendWithoutResultAsync(HttpMethod.Put, $"admin/realms/{Uri.EscapeDataString(realm)}", ct, changes);
    public Task<JsonElement> GetClientsAsync(string realm, string clientId, CancellationToken ct = default) => GetJsonAsync($"admin/realms/{Uri.EscapeDataString(realm)}/clients?clientId={Uri.EscapeDataString(clientId)}", ct);
    public async Task<JsonElement?> GetUserAsync(string realm, string userId, CancellationToken ct = default) {
        using var response = await SendAsync(HttpMethod.Get, $"admin/realms/{Uri.EscapeDataString(realm)}/users/{Uri.EscapeDataString(userId)}", ct, expectedNotFoundOk: true);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }
    public Task<JsonElement> GetRealmRoleUsersAsync(string realm, string role, int first, int max, CancellationToken ct = default) =>
        GetJsonAsync($"admin/realms/{Uri.EscapeDataString(realm)}/roles/{Uri.EscapeDataString(role)}/users?first={first}&max={max}", ct);
    public Task<JsonElement> FindUsersByEmailAsync(string realm, string email, CancellationToken ct = default) => GetJsonAsync($"admin/realms/{Uri.EscapeDataString(realm)}/users?email={Uri.EscapeDataString(email)}&exact=true", ct);
    public Task<JsonElement> GetUserRealmRolesAsync(string realm, string userId, CancellationToken ct = default) => GetJsonAsync($"admin/realms/{Uri.EscapeDataString(realm)}/users/{Uri.EscapeDataString(userId)}/role-mappings/realm", ct);
    // Keycloak 26.8.1 UserResource /groups and RoleMapperResource /realm/composite.
    public Task<JsonElement> GetEffectiveUserRealmRolesAsync(string realm, string userId, CancellationToken ct = default) => GetJsonAsync($"admin/realms/{Uri.EscapeDataString(realm)}/users/{Uri.EscapeDataString(userId)}/role-mappings/realm/composite", ct);
    public Task<JsonElement> GetEmployeeGroupAsync(string realm, string fixedName, CancellationToken ct = default)
    {
        if (fixedName is not ("managed-users" or "app-employee" or "app-manager" or "app-admin"))
            throw new KeycloakException("Only fixed employee group names may be resolved");
        return GetJsonAsync($"admin/realms/{Uri.EscapeDataString(realm)}/group-by-path/{fixedName}", ct);
    }
    public Task<JsonElement> GetGroupsAsync(string realm, int first, int max, CancellationToken ct = default) => GetJsonAsync($"admin/realms/{Uri.EscapeDataString(realm)}/groups?first={first}&max={max}&briefRepresentation=false", ct);
    public Task<JsonElement> GetUserGroupsAsync(string realm, string userId, CancellationToken ct = default) => GetJsonAsync($"admin/realms/{Uri.EscapeDataString(realm)}/users/{Uri.EscapeDataString(userId)}/groups?first=0&max=101&briefRepresentation=false", ct);
    public Task<JsonElement> GetGroupMembersAsync(string realm, string groupId, int first, int max, CancellationToken ct = default) => GetJsonAsync($"admin/realms/{Uri.EscapeDataString(realm)}/groups/{Uri.EscapeDataString(groupId)}/members?first={first}&max={max}", ct);
    public Task<JsonElement> GetGroupRoleMappingsAsync(string realm, string groupId, CancellationToken ct = default) => GetJsonAsync($"admin/realms/{Uri.EscapeDataString(realm)}/groups/{Uri.EscapeDataString(groupId)}/role-mappings", ct);
    public Task<JsonElement> GetEffectiveGroupRealmRolesAsync(string realm, string groupId, CancellationToken ct = default) => GetJsonAsync($"admin/realms/{Uri.EscapeDataString(realm)}/groups/{Uri.EscapeDataString(groupId)}/role-mappings/realm/composite", ct);
    public Task<string> CreateGroupAsync(string realm, object group, CancellationToken ct = default) => SendForLocationAsync(HttpMethod.Post, $"admin/realms/{Uri.EscapeDataString(realm)}/groups", ct, group);
    public Task AssignGroupRealmRolesAsync(string realm, string groupId, object[] roles, CancellationToken ct = default) => SendWithoutResultAsync(HttpMethod.Post, $"admin/realms/{Uri.EscapeDataString(realm)}/groups/{Uri.EscapeDataString(groupId)}/role-mappings/realm", ct, roles);
    public Task CreateAuthorizationUserPolicyAsync(string realm, string authorizationClientId, object policy, CancellationToken ct = default) => SendWithoutResultAsync(HttpMethod.Post, $"admin/realms/{Uri.EscapeDataString(realm)}/clients/{Uri.EscapeDataString(authorizationClientId)}/authz/resource-server/policy/user", ct, policy);
    public Task CreateAuthorizationScopePermissionAsync(string realm, string authorizationClientId, object permission, CancellationToken ct = default) => SendWithoutResultAsync(HttpMethod.Post, $"admin/realms/{Uri.EscapeDataString(realm)}/clients/{Uri.EscapeDataString(authorizationClientId)}/authz/resource-server/permission/scope", ct, permission);
    public Task AddUserToGroupAsync(string realm, string userId, string groupId, CancellationToken ct = default) => SendWithoutResultAsync(HttpMethod.Put, $"admin/realms/{Uri.EscapeDataString(realm)}/users/{Uri.EscapeDataString(userId)}/groups/{Uri.EscapeDataString(groupId)}", ct);
    public Task RemoveUserFromGroupAsync(string realm, string userId, string groupId, CancellationToken ct = default) => SendWithoutResultAsync(HttpMethod.Delete, $"admin/realms/{Uri.EscapeDataString(realm)}/users/{Uri.EscapeDataString(userId)}/groups/{Uri.EscapeDataString(groupId)}", ct);
    public Task RemoveRealmRolesAsync(string realm, string userId, object[] roles, CancellationToken ct = default) => SendWithoutResultAsync(HttpMethod.Delete, $"admin/realms/{Uri.EscapeDataString(realm)}/users/{Uri.EscapeDataString(userId)}/role-mappings/realm", ct, roles);
    public Task LogoutUserAsync(string realm, string userId, CancellationToken ct = default) => SendWithoutResultAsync(HttpMethod.Post, $"admin/realms/{Uri.EscapeDataString(realm)}/users/{Uri.EscapeDataString(userId)}/logout", ct);
    public Task SendExecuteActionsEmailAsync(string realm, string userId, string clientId, string redirectUri, int lifespanSeconds, string[] actions, CancellationToken ct = default) =>
        SendWithoutResultAsync(HttpMethod.Put, $"admin/realms/{Uri.EscapeDataString(realm)}/users/{Uri.EscapeDataString(userId)}/execute-actions-email?client_id={Uri.EscapeDataString(clientId)}&redirect_uri={Uri.EscapeDataString(redirectUri)}&lifespan={lifespanSeconds}", ct, actions);
    public Task<JsonElement> GetServiceAccountAsync(string realm, string clientId, CancellationToken ct = default) =>
        GetJsonAsync($"admin/realms/{Uri.EscapeDataString(realm)}/clients/{Uri.EscapeDataString(clientId)}/service-account-user", ct);
    public Task<JsonElement> GetClientRoleAsync(string realm, string clientId, string role, CancellationToken ct = default) =>
        GetJsonAsync($"admin/realms/{Uri.EscapeDataString(realm)}/clients/{Uri.EscapeDataString(clientId)}/roles/{Uri.EscapeDataString(role)}", ct);
    public Task AssignClientRolesAsync(string realm, string userId, string clientId, object[] roles, CancellationToken ct = default) =>
        SendWithoutResultAsync(HttpMethod.Post, $"admin/realms/{Uri.EscapeDataString(realm)}/users/{Uri.EscapeDataString(userId)}/role-mappings/clients/{Uri.EscapeDataString(clientId)}", ct, roles);
    private async Task<JsonElement> GetJsonAsync(string path, CancellationToken ct) {
        using var response = await SendAsync(HttpMethod.Get, path, ct); return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }
    private async Task SendWithoutResultAsync(HttpMethod method, string path, CancellationToken ct, object? body = null) {
        using var response = await SendAsync(method, path, ct, body);
    }

    private void ValidatePath(HttpMethod method, string path)
    {
        if (_allowedRealm is null) return; // Explicit platform provisioner only.
        // Reject segments which URI/proxy normalization could turn into another
        // endpoint. Query values may legitimately contain encoded email/redirects.
        var pathOnly = path.Split('?', 2)[0];
        if (pathOnly.Contains('\\') || pathOnly.Contains('#') || pathOnly.StartsWith('/')
            || pathOnly.Split('/').Any(segment => {
                var decoded = Uri.UnescapeDataString(segment);
                return decoded is "" or "." or ".." || decoded.Contains('/') || decoded.Contains('\\')
                    || decoded.Contains('%') || decoded.Contains('?') || decoded.Contains('#')
                    || decoded.Any(char.IsControl);
            }))
            throw new KeycloakException("Tenant identity request path is not canonical");
        var prefix = $"admin/realms/{_allowedRealm}";
        if (!path.StartsWith(prefix + "/", StringComparison.Ordinal) && path != prefix)
            throw new KeycloakException("Tenant identity credentials cannot access another realm");
        var relative = path[prefix.Length..];
        // Local guard rejects configuration writes before network access. The
        // extension independently enforces its narrower operation/target allowlist.
        if (method != HttpMethod.Get && (!(relative == "/users" || relative.StartsWith("/users/", StringComparison.Ordinal))
            || relative.StartsWith("/users/profile", StringComparison.Ordinal)))
            throw new KeycloakException("Tenant identity credentials cannot modify realm configuration");
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        CancellationToken ct,
        object? body = null,
        bool expectedNotFoundOk = false)
    {
        ValidatePath(method, path);
        path = TransportPath(path);
        var token = await GetAdminTokenAsync(ct);
        using var request = new HttpRequestMessage(method, $"{_serverUrl}/{path}");
        request.Headers.Authorization = new("Bearer", token);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode || (expectedNotFoundOk && response.StatusCode == System.Net.HttpStatusCode.NotFound))
            return response;

        throw new KeycloakException($"Keycloak {method} request failed: {(int)response.StatusCode}", response.StatusCode);
    }

    private async Task<string> SendForLocationAsync(
        HttpMethod method,
        string path,
        CancellationToken ct,
        object body)
    {
        ValidatePath(method, path);
        path = TransportPath(path);
        var token = await GetAdminTokenAsync(ct);
        using var request = new HttpRequestMessage(method, $"{_serverUrl}/{path}");
        request.Headers.Authorization = new("Bearer", token);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new KeycloakException($"Keycloak {method} request failed: {(int)response.StatusCode}", response.StatusCode);

        var location = response.Headers.Location?.ToString()
            ?? throw new KeycloakException("Location header missing");
        return location.Split('/')[^1];
    }

    private string TransportPath(string path)
    {
        if (_allowedRealm is null) return path;
        // ValidatePath has already checked the exact realm and canonical segments.
        // A missing/denied extension is an error, never a reason to retry Admin REST.
        var prefix = $"admin/realms/{_allowedRealm}";
        return $"realms/{_allowedRealm}/cleaning-identity/v1" + path[prefix.Length..];
    }
}

public class KeycloakException : Exception
{
    public System.Net.HttpStatusCode? StatusCode { get; }
    public KeycloakException(string message, System.Net.HttpStatusCode? statusCode = null) : base(message) { StatusCode = statusCode; }
}
