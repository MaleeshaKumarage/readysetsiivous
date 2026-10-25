using System.Net.Http.Json;
using System.Text.Json;
using CleaningSuite.Infrastructure.Keycloak;
namespace CleaningSuite.Tests.Auth;
internal sealed class SyntheticKeycloakBootstrap(HttpClient http, string url, string username, string password)
    : KeycloakAdminClient(http, url, "master", "test-bootstrap", "")
{
    public override async Task<string> GetAdminTokenAsync(CancellationToken ct = default)
    {
        using var response = await http.PostAsync(url + "/realms/master/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string,string> {
                ["grant_type"]="password", ["client_id"]="admin-cli", ["username"]=username, ["password"]=password }), ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return body.GetProperty("access_token").GetString()!;
    }
}
