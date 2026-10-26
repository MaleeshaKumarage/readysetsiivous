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

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(null, null, null)]
    public async Task ProvisioningNormalizesSmtpBooleanSettingsAndDefaults(bool? auth, bool? startTls, bool? ssl)
    {
        var transport = new KeycloakTransport();
        var flags = new Dictionary<string, bool>();
        if (auth.HasValue) flags["Auth"] = auth.Value;
        if (startTls.HasValue) flags["StartTls"] = startTls.Value;
        if (ssl.HasValue) flags["Ssl"] = ssl.Value;
        using var settings = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new { EmployeeInvites = new { Smtp = flags } }));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Auth:Keycloak:ProvisioningSetupReviewed"] = "true",
            ["Auth:Keycloak:Audience"] = "cleaning-suite-api",
            ["Auth:Keycloak:PlatformAudience"] = "cleaning-suite-platform-api",
            ["Auth:Keycloak:ServerUrl"] = "https://auth.test",
            ["Auth:Keycloak:TenantAccounts:tenant-test:ClientId"] = "tenant-runtime",
            ["Auth:Keycloak:TenantAccounts:tenant-test:ClientSecret"] = "synthetic-runtime-secret",
            ["EmployeeInvites:Smtp:Host"] = "mail.example.invalid",
            ["EmployeeInvites:Smtp:From"] = "sender@example.invalid",
            ["EmployeeInvites:Smtp:User"] = "synthetic-user",
            ["EmployeeInvites:Smtp:Password"] = "synthetic-password",
        }).AddJsonStream(settings).Build();
        var admin = new KeycloakAdminClient(new HttpClient(transport), "https://auth.test", "master", "provisioner", "secret");
        await new KeycloakProvisioner(new PlatformKeycloakClient(admin), NullLogger<KeycloakProvisioner>.Instance,
            new KeycloakResourceOptions(config), config)
            .ProvisionAsync("tenant-test", "Test", "owner@example.invalid", "Test", "Owner", new[] { "https://site.test/*" });
        using var realm = JsonDocument.Parse(Assert.Single(transport.RealmUpdates));
        var smtp = realm.RootElement.GetProperty("smtpServer");
        Assert.Equal((auth ?? true) ? "true" : "false", smtp.GetProperty("auth").GetString());
        Assert.Equal((startTls ?? true) ? "true" : "false", smtp.GetProperty("starttls").GetString());
        Assert.Equal((ssl ?? false) ? "true" : "false", smtp.GetProperty("ssl").GetString());
        Assert.Equal(auth ?? true, smtp.TryGetProperty("password", out _));
    }

    private sealed class KeycloakTransport : HttpMessageHandler
    {
        public List<string> Clients { get; } = new();
        public List<string> RealmUpdates { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/token")) return Json(new { access_token = "test-token", expires_in = 3600 });
            if (request.Method == HttpMethod.Put && path == "/admin/realms/tenant-test")
                RealmUpdates.Add(await request.Content!.ReadAsStringAsync(ct));
            if (request.Method == HttpMethod.Get && path.EndsWith("/clients")) return Json(new[] { new { id = "client-id" } });
            if (request.Method == HttpMethod.Get && path.EndsWith("/service-account-user")) return Json(new { id = "service-user" });
            if (request.Method == HttpMethod.Get && path.EndsWith("/users/profile")) return Json(new { attributes = Array.Empty<object>() });
            if (request.Method == HttpMethod.Get && path.Contains("/clients/client-id/roles/")) return Json(new { id = "client-role", name = path.Split('/').Last() });
            if (request.Method == HttpMethod.Post && path.EndsWith("/clients")) Clients.Add(await request.Content!.ReadAsStringAsync(ct));
            if (request.Method == HttpMethod.Get && path.EndsWith("/roles/admin")) return Json(new { id = "admin-role", name = "admin" });
            var response = new HttpResponseMessage(HttpStatusCode.Created);
            if (path.EndsWith("/users")) response.Headers.Location = new Uri("https://auth.test/admin/realms/tenant-test/users/user-1");
            return response;
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    }
}
