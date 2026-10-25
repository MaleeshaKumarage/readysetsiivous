using System.Net;
using System.Text.Json;
using CleaningSuite.Infrastructure.Auth;
using CleaningSuite.Infrastructure.Keycloak;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace CleaningSuite.Tests.Auth;

public class KeycloakAudienceProvisioningTests
{
    [Fact]
    public void DevImportAndRuntimeDefaultsAgreeOnResourcesAndIdTokenSeparation()
    {
        var apiRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        using var import = JsonDocument.Parse(File.ReadAllText(Path.Combine(apiRoot, "deploy/keycloak-realm.json")));
        using var runtime = JsonDocument.Parse(File.ReadAllText(Path.Combine(apiRoot, "CleaningSuite.Api/appsettings.json")));
        var auth = runtime.RootElement.GetProperty("Auth").GetProperty("Keycloak");
        Assert.Equal("cleaning-suite-api", auth.GetProperty("Audience").GetString());
        Assert.Equal("cleaning-suite-platform-api", auth.GetProperty("PlatformAudience").GetString());
        Assert.False(auth.TryGetProperty("Audiences", out _));
        var client = Assert.Single(import.RootElement.GetProperty("clients").EnumerateArray().Where(c => c.GetProperty("clientId").GetString() == "cleaning-suite-web"));
        var mapper = Assert.Single(client.GetProperty("protocolMappers").EnumerateArray().Where(m => m.GetProperty("protocolMapper").GetString() == "oidc-audience-mapper"));
        Assert.Equal("cleaning-suite-api", mapper.GetProperty("config").GetProperty("included.custom.audience").GetString());
        Assert.Equal("false", mapper.GetProperty("config").GetProperty("id.token.claim").GetString());
    }

    [Fact]
    public async Task ProvisioningUsesConfiguredApiAudienceWithoutAddingItToIdTokens()
    {
        var transport = new KeycloakTransport();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Auth:Keycloak:Audience"] = "custom-tenant-api", ["Auth:Keycloak:PlatformAudience"] = "custom-platform-api"
        }).Build();
        var admin = new KeycloakAdminClient(new HttpClient(transport), "https://auth.test", "master", "provisioner", "secret");
        await new KeycloakProvisioner(new PlatformKeycloakClient(admin), NullLogger<KeycloakProvisioner>.Instance, new KeycloakResourceOptions(config))
            .ProvisionAsync("tenant-test", "Test", "owner@example.invalid", "Test", "Owner", new[] { "https://site.test/*" });
        using var client = JsonDocument.Parse(Assert.Single(transport.Clients));
        var mapper = Assert.Single(client.RootElement.GetProperty("protocolMappers").EnumerateArray());
        Assert.Equal("oidc-audience-mapper", mapper.GetProperty("protocolMapper").GetString());
        var mapperConfig = mapper.GetProperty("config");
        Assert.Equal("custom-tenant-api", mapperConfig.GetProperty("included.custom.audience").GetString());
        Assert.Equal("true", mapperConfig.GetProperty("access.token.claim").GetString());
        Assert.Equal("false", mapperConfig.GetProperty("id.token.claim").GetString());
        Assert.DoesNotContain("custom-platform-api", Assert.Single(transport.Clients));
    }

    private sealed class KeycloakTransport : HttpMessageHandler
    {
        public List<string> Clients { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/token")) return Json(new { access_token = "test-token", expires_in = 3600 });
            if (request.Method == HttpMethod.Post && path.EndsWith("/clients")) Clients.Add(await request.Content!.ReadAsStringAsync(ct));
            if (request.Method == HttpMethod.Get && path.EndsWith("/roles/admin")) return Json(new { id = "admin-role", name = "admin" });
            var response = new HttpResponseMessage(HttpStatusCode.Created);
            if (path.EndsWith("/users")) response.Headers.Location = new Uri("https://auth.test/admin/realms/tenant-test/users/user-1");
            return response;
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    }
}
