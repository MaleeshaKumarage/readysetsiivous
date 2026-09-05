using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace CleaningSuite.Infrastructure.Keycloak;

/// <summary>
/// Thin wrapper over Keycloak Admin REST. Admin token via provisioner client
/// credentials (master realm), falling back to master admin password grant.
/// </summary>
public class KeycloakAdminClient
{
    private readonly HttpClient _http;
    private readonly string _serverUrl;
    private readonly string _provisionerRealm;
    private readonly string _provisionerClientId;
    private readonly string _provisionerClientSecret;
    private readonly string? _adminUser;
    private readonly string? _adminPassword;

    private string? _cachedToken;
    private DateTime _tokenExpiresUtc = DateTime.MinValue;

    public KeycloakAdminClient(
        HttpClient http,
        string serverUrl,
        string provisionerRealm,
        string provisionerClientId,
        string provisionerClientSecret,
        string? adminUser = null,
        string? adminPassword = null)
    {
        _http = http;
        _serverUrl = serverUrl.TrimEnd('/');
        _provisionerRealm = provisionerRealm;
        _provisionerClientId = provisionerClientId;
        _provisionerClientSecret = provisionerClientSecret;
        _adminUser = adminUser;
        _adminPassword = adminPassword;
    }

    public async Task<string> GetAdminTokenAsync(CancellationToken ct = default)
    {
        if (_cachedToken is not null && DateTime.UtcNow < _tokenExpiresUtc.AddMinutes(-5))
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
        else if (_adminUser is not null)
        {
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "admin-cli",
                ["username"] = _adminUser,
                ["password"] = _adminPassword ?? "",
            });
        }
        else
        {
            throw new InvalidOperationException("Keycloak admin credentials not configured");
        }

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new KeycloakException($"Admin token failed: {(int)response.StatusCode}");

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        _cachedToken = payload.GetProperty("access_token").GetString()
            ?? throw new KeycloakException("access_token missing");
        var expiresIn = payload.GetProperty("expires_in").GetInt32();
        _tokenExpiresUtc = DateTime.UtcNow.AddSeconds(expiresIn);

        return _cachedToken;
    }

    public async Task<bool> RealmExistsAsync(string realm, CancellationToken ct = default)
    {
        var response = await SendAsync(HttpMethod.Get, $"admin/realms/{realm}", ct, expectedNotFoundOk: true);
        return response.IsSuccessStatusCode;
    }

    public Task CreateRealmAsync(string realm, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "admin/realms", ct, new
        {
            realm,
            enabled = true,
        });

    public Task CreateRoleAsync(string realm, string roleName, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"admin/realms/{realm}/roles", ct, new { name = roleName });

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

    public Task UpdateUserAsync(string realm, string userId, object user, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, $"admin/realms/{realm}/users/{userId}", ct, user);

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

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        CancellationToken ct,
        object? body = null,
        bool expectedNotFoundOk = false)
    {
        var token = await GetAdminTokenAsync(ct);
        using var request = new HttpRequestMessage(method, $"{_serverUrl}/{path}");
        request.Headers.Authorization = new("Bearer", token);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode || (expectedNotFoundOk && response.StatusCode == System.Net.HttpStatusCode.NotFound))
            return response;

        throw new KeycloakException($"Keycloak {method} {path} failed: {(int)response.StatusCode}");
    }

    private async Task<string> SendForLocationAsync(
        HttpMethod method,
        string path,
        CancellationToken ct,
        object body)
    {
        var token = await GetAdminTokenAsync(ct);
        using var request = new HttpRequestMessage(method, $"{_serverUrl}/{path}");
        request.Headers.Authorization = new("Bearer", token);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new KeycloakException($"Keycloak {method} {path} failed: {(int)response.StatusCode}");

        var location = response.Headers.Location?.ToString()
            ?? throw new KeycloakException("Location header missing");
        return location.Split('/')[^1];
    }
}

public class KeycloakException : Exception
{
    public KeycloakException(string message) : base(message) { }
}
