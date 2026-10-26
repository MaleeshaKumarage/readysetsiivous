using System.Net.Http.Json;
using System.Text.Json;
using CleaningSuite.Infrastructure.Keycloak;
namespace CleaningSuite.Tests.Auth;
internal sealed class SyntheticKeycloakBootstrap(HttpClient http, string url, string username, string password)
    : KeycloakAdminClient(http, url, "master", "test-bootstrap", "")
{
    private readonly SemaphoreSlim tokenLock = new(1, 1);
    private string? token;
    private DateTimeOffset tokenExpires;
    public override async Task<string> GetAdminTokenAsync(CancellationToken ct = default)
    {
        await tokenLock.WaitAsync(ct);
        try {
        if (token is not null && DateTimeOffset.UtcNow < tokenExpires) return token;
        using var response = await http.PostAsync(url + "/realms/master/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string,string> {
                ["grant_type"]="password", ["client_id"]="admin-cli", ["username"]=username, ["password"]=password }), ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        token = body.GetProperty("access_token").GetString()!;
        tokenExpires = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, body.GetProperty("expires_in").GetInt32() - 10));
        return token;
        } finally { tokenLock.Release(); }
    }
}
