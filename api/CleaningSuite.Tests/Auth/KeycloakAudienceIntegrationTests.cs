using System.Net.Http.Json;
using System.Text.Json;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Tenants;
using CleaningSuite.Infrastructure.Auth;
using CleaningSuite.Infrastructure.Keycloak;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace CleaningSuite.Tests.Auth;

public sealed class AudienceKeycloakFactAttribute : FactAttribute
{
    public AudienceKeycloakFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AUDIENCE_TEST_KEYCLOAK_URL"))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AUDIENCE_TEST_DATABASE")))
            Skip = "Requires owned disposable AUDIENCE_TEST_KEYCLOAK_URL and AUDIENCE_TEST_DATABASE.";
    }
}

public class KeycloakAudienceIntegrationTests
{
    [AudienceKeycloakFact]
    public async Task ProvisionedRealmIssuesApiAudienceOnLoginAndRefreshButNotIdToken()
    {
        var url = Environment.GetEnvironmentVariable("AUDIENCE_TEST_KEYCLOAK_URL")!;
        var frontend = Environment.GetEnvironmentVariable("AUDIENCE_TEST_FRONTEND_URL") ?? "http://127.0.0.1:5767";
        var realm = "audience-test-" + Guid.NewGuid().ToString("N")[..8];
        var email = "owner@" + realm + ".invalid";
        const string password = "AudienceOwnerTestOnly2026";
        using var http = new HttpClient();
        var admin = new KeycloakAdminClient(http, url, "master", "outbound-admin", "", "audience-test-admin", "audience_test_only_2026");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Auth:Keycloak:Audience"] = "cleaning-suite-api", ["Auth:Keycloak:PlatformAudience"] = "cleaning-suite-platform-api"
        }).Build();
        await new KeycloakProvisioner(admin, NullLogger<KeycloakProvisioner>.Instance, new KeycloakResourceOptions(config))
            .ProvisionAsync(realm, "Audience review", email, "Audience", "Owner", new[] { frontend + "/*" });
        var id = (await admin.GetUserIdByUsernameAsync(realm, email))!;
        await admin.UpdateUserAsync(realm, id, new { emailVerified = true, requiredActions = Array.Empty<string>(), credentials = new[] { new { type = "password", value = password, temporary = false } } });
        foreach (var (client, audience) in new[] { ("audience-missing", (string?)null), ("audience-wrong", "other-resource") })
            await admin.CreateClientAsync(realm, new { clientId = client, enabled = true, publicClient = true, directAccessGrantsEnabled = true,
                protocolMappers = audience is null ? Array.Empty<object>() : new[] { KeycloakProvisioner.AudienceMapper(audience) } });
        var employeeId = await admin.CreateUserAsync(realm, new { username = "employee@" + realm + ".invalid", email = "employee@" + realm + ".invalid", firstName = "Test", lastName = "Employee", emailVerified = true, enabled = true,
            credentials = new[] { new { type = "password", value = password, temporary = false } } });
        var role = await admin.GetRoleAsync(realm, "employee");
        await admin.AssignRealmRoleAsync(realm, employeeId, new object[] { role });
        using var store = DocumentStore.For(options => { options.Connection(Environment.GetEnvironmentVariable("AUDIENCE_TEST_DATABASE")!); options.Policies.AllDocumentsAreMultiTenanted(); });
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        using (var registry = store.LightweightSession("registry")) { registry.Store(new TenantRegistration { Slug = realm, Status = TenantRegistration.StatusActive }); await registry.SaveChangesAsync(); }
        using (var tenant = store.LightweightSession(realm)) { tenant.Store(new Employee { Email = "employee@" + realm + ".invalid", KeycloakUserId = employeeId, Role = "employee" }); await tenant.SaveChangesAsync(); }
        var token = await Login(http, url, realm, email, password);
        Assert.Contains("cleaning-suite-api", Audiences(token.GetProperty("access_token").GetString()!));
        Assert.DoesNotContain("cleaning-suite-platform-api", Audiences(token.GetProperty("access_token").GetString()!));
        Assert.DoesNotContain("cleaning-suite-api", Audiences(token.GetProperty("id_token").GetString()!));
        using var refreshed = await http.PostAsync(url + "/realms/" + realm + "/protocol/openid-connect/token", new FormUrlEncodedContent(new Dictionary<string, string> {
            ["grant_type"] = "refresh_token", ["client_id"] = "cleaning-suite-web", ["refresh_token"] = token.GetProperty("refresh_token").GetString()!
        }));
        refreshed.EnsureSuccessStatusCode();
        var refresh = await refreshed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("cleaning-suite-api", Audiences(refresh.GetProperty("access_token").GetString()!));
        foreach (var client in new[] { "audience-missing", "audience-wrong" }) {
            var wrong = await Login(http, url, realm, email, password, client);
            Assert.DoesNotContain("cleaning-suite-api", Audiences(wrong.GetProperty("access_token").GetString()!));
        }
        var artifact = Environment.GetEnvironmentVariable("AUDIENCE_TEST_CONTEXT_PATH");
        if (!string.IsNullOrWhiteSpace(artifact)) await File.WriteAllTextAsync(artifact, JsonSerializer.Serialize(new { realm, email, password, frontend, keycloakUrl = url, employeeEmail = "employee@" + realm + ".invalid" }));
    }
    private static string[] Audiences(string token) => new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token).Audiences.ToArray();
    private static async Task<JsonElement> Login(HttpClient http, string url, string realm, string email, string password, string client = "cleaning-suite-web")
    {
        using var response = await http.PostAsync(url + "/realms/" + realm + "/protocol/openid-connect/token", new FormUrlEncodedContent(new Dictionary<string, string> {
            ["grant_type"] = "password", ["client_id"] = client, ["username"] = email, ["password"] = password, ["scope"] = "openid email"
        }));
        response.EnsureSuccessStatusCode(); return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
