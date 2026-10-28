using System.Net;
using System.Net.Http.Json;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Infrastructure.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using CleaningSuite.Application.Employees;
using CleaningSuite.Infrastructure.Leads;
using Moq;
using CleaningSuite.Infrastructure.Keycloak;
using Microsoft.Extensions.Configuration;
namespace CleaningSuite.Tests.Auth;
public class TenantKeycloakClientsTests
{
    [Theory]
    [InlineData("master")][InlineData("TENANT-A")][InlineData("tenant-a/../master")][InlineData("tenant%2da")]
    public void InvalidOrPlatformRealmsNeverResolve(string realm)
    {
        var factory = new TenantKeycloakClients(new HttpClient(), Config());
        Assert.Throws<KeycloakException>(() => factory.ForTenant(realm));
    }
    [Fact]
    public void MissingTenantSecretDoesNotFallBackToConfiguredPlatform()
    {
        Assert.Throws<KeycloakException>(() => new TenantKeycloakClients(new HttpClient(), Config()).ForTenant("tenant-b"));
    }
    [Fact]
    public async Task TenantClientRejectsOtherRealmsAndConfigurationWritesBeforeNetwork()
    {
        var handler = new RecordingHandler();
        var client = new TenantKeycloakClients(new HttpClient(handler), Config()).ForTenant("tenant-a");
        await Assert.ThrowsAsync<KeycloakException>(() => client.GetRealmAsync("tenant-b"));
        await Assert.ThrowsAsync<KeycloakException>(() => client.GetRealmAsync("master"));
        await Assert.ThrowsAsync<KeycloakException>(() => client.UpdateRealmAsync("tenant-a", new { enabled = false }));
        await Assert.ThrowsAsync<KeycloakException>(() => client.UpdateUserProfileAsync("tenant-a", new {}));
        await Assert.ThrowsAsync<KeycloakException>(() => client.CreateClientAsync("tenant-a", new {}));
        Assert.Empty(handler.Paths);
        await client.FindUsersByEmailAsync("tenant-a", "synthetic@example.invalid");
        Assert.Equal("/realms/tenant-a/protocol/openid-connect/token", handler.Paths[0]);
        Assert.Equal("/realms/tenant-a/cleaning-identity/v1/users", handler.Paths[1]);
        Assert.DoesNotContain(handler.Bodies, body => body.Contains("grant_type=password"));
    }
    [Theory]
    [InlineData("../master")][InlineData("%2e%2e")][InlineData("..%2f..%2fmaster")]
    [InlineData("%252e%252e%252fmaster")][InlineData("..\\..\\master")][InlineData(".")]
    public async Task TraversalSegmentsAreRejectedBeforeTokenOrHttp(string segment)
    {
        var handler = new RecordingHandler();
        var client = new TenantKeycloakClients(new HttpClient(handler), Config()).ForTenant("tenant-a");
        // Older public methods interpolate these identifiers; the shared guard must
        // inspect the path before System.Uri can collapse or decode it.
        await Assert.ThrowsAsync<KeycloakException>(() => client.GetRoleAsync("tenant-a", segment));
        await Assert.ThrowsAsync<KeycloakException>(() => client.AssignRealmRoleAsync("tenant-a", segment, []));
        await Assert.ThrowsAsync<KeycloakException>(() => client.SetRealmEnabledAsync("tenant-a/../master", false));
        Assert.Empty(handler.Paths);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ProvisioningWithoutReviewedSetupOrCredentialsCreatesNoRealm(bool reviewed)
    {
        var handler = new RecordingHandler();
        var config = new ConfigurationBuilder().AddConfiguration(Config()).AddInMemoryCollection(new Dictionary<string,string?> {
            ["Auth:Keycloak:Audience"]="tenant-api", ["Auth:Keycloak:PlatformAudience"]="platform-api",
            ["Auth:Keycloak:ProvisioningSetupReviewed"]=reviewed.ToString()
        }).Build();
        var admin = new KeycloakAdminClient(new HttpClient(handler), "https://auth.invalid", "master", "platform-provisioner", "synthetic-secret");
        var provisioner = new KeycloakProvisioner(new PlatformKeycloakClient(admin), NullLogger<KeycloakProvisioner>.Instance,
            new KeycloakResourceOptions(config), config);
        await Assert.ThrowsAsync<TenantIdentitySetupRequiredException>(() => provisioner.ProvisionAsync("tenant-b", "Synthetic", "owner@example.invalid", "Test", "Owner", []));
        Assert.Empty(handler.Paths);
    }
    [Fact]
    public async Task LeadDirectoryUsesOnlyItsTenantAccount()
    {
        var handler = new RecordingHandler();
        var tenant = new Mock<ITenantContext>(); tenant.SetupGet(x => x.TenantId).Returns("tenant-a");
        var directory = new LeadAdminDirectory(new TenantKeycloakClients(new HttpClient(handler), Config()),
            new Mock<IEmployeeRepository>().Object, tenant.Object);
        await Assert.ThrowsAsync<CleaningSuite.Application.Leads.LeadConflictException>(() => directory.ListAsync(CancellationToken.None));
        Assert.Contains("/realms/tenant-a/protocol/openid-connect/token", handler.Paths);
        Assert.Contains("/realms/tenant-a/cleaning-identity/v1/group-by-path/managed-users", handler.Paths);
        Assert.DoesNotContain(handler.Paths, path => path.Contains("/master/"));
    }
    private static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
        ["Auth:Keycloak:ServerUrl"]="https://auth.invalid", ["Auth:Keycloak:ProvisionerRealm"]="master",
        ["Auth:Keycloak:ProvisionerClientSecret"]="synthetic-platform-secret",
        ["Auth:Keycloak:TenantAccounts:tenant-a:ClientId"]="employee-runtime",
        ["Auth:Keycloak:TenantAccounts:tenant-a:ClientSecret"]="synthetic-tenant-secret"
    }).Build();
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Paths { get; } = []; public List<string> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(request.RequestUri.AbsolutePath.EndsWith("/token")
                ? (object)new { access_token="synthetic-token", expires_in=3600 } : Array.Empty<object>()) };
        }
    }
}
