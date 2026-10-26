using System.Net;
using System.Text.Json;
using CleaningSuite.Infrastructure.Auth;
using CleaningSuite.Infrastructure.Keycloak;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace CleaningSuite.Tests.Auth;

public class KeycloakProvisioningRecoveryTests
{
    [Fact]
    public async Task FailedNewRealmIsRemovedAndRetryCanFinish()
    {
        var transport = new RecoveryTransport { FailRoleOnce = true };
        var provisioner = Provisioner(transport);
        await Assert.ThrowsAsync<KeycloakException>(() => Create(provisioner));
        Assert.False(transport.Exists); Assert.Equal(1, transport.Deletes);
        await Create(provisioner);
        Assert.True(transport.Exists); Assert.Equal(1, transport.Deletes);
    }

    [Fact]
    public async Task ExistingRealmConflictNeverDeletesPreexistingTenant()
    {
        var transport = new RecoveryTransport { Exists = true };
        await Assert.ThrowsAsync<KeycloakException>(() => Create(Provisioner(transport)));
        Assert.True(transport.Exists); Assert.Equal(0, transport.Deletes);
    }

    [Fact]
    public async Task FailedCreateNeverDeletesAnUnconfirmedRealm()
    {
        var transport = new RecoveryTransport { FailCreate = true };
        await Assert.ThrowsAsync<KeycloakException>(() => Create(Provisioner(transport)));
        Assert.Equal(0, transport.Deletes);
    }

    [Fact]
    public async Task CanceledProvisioningUsesIndependentCleanupDeadline()
    {
        using var cancellation = new CancellationTokenSource();
        var transport = new RecoveryTransport { CancelOnRole = cancellation };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(Provisioner(transport), cancellation.Token));
        Assert.False(transport.Exists); Assert.Equal(1, transport.Deletes);
    }

    [Fact]
    public async Task FailedCleanupLeavesRealmForExplicitReconciliationWithoutDeletingOnRetry()
    {
        var transport = new RecoveryTransport { FailRoleOnce = true, FailDelete = true };
        var provisioner = Provisioner(transport);
        var original = await Assert.ThrowsAsync<KeycloakException>(() => Create(provisioner));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, original.StatusCode);
        Assert.True(transport.Exists); Assert.Equal(1, transport.Deletes);
        await Assert.ThrowsAsync<KeycloakException>(() => Create(provisioner));
        Assert.True(transport.Exists); Assert.Equal(1, transport.Deletes);
    }

    private static KeycloakProvisioner Provisioner(RecoveryTransport transport)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Auth:Keycloak:Audience"] = "cleaning-suite-api", ["Auth:Keycloak:PlatformAudience"] = "cleaning-suite-platform-api"
        }).Build();
        return new(new PlatformKeycloakClient(new(new HttpClient(transport), "https://auth.test", "master", "provisioner", "synthetic")),
            NullLogger<KeycloakProvisioner>.Instance, new KeycloakResourceOptions(config));
    }
    private static Task Create(KeycloakProvisioner provisioner, CancellationToken ct = default) =>
        provisioner.ProvisionAsync("tenant-test", "Test", "owner@example.invalid", "Test", "Owner", ["https://site.test/*"], ct);

    private sealed class RecoveryTransport : HttpMessageHandler
    {
        public bool Exists, FailRoleOnce, FailDelete, FailCreate;
        public int Deletes;
        public CancellationTokenSource? CancelOnRole;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/token")) return Json(new { access_token = "token", expires_in = 3600 });
            if (request.Method == HttpMethod.Post && path == "/admin/realms") {
                if (FailCreate) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                if (Exists) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict));
                Exists = true; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));
            }
            if (request.Method == HttpMethod.Delete && path == "/admin/realms/tenant-test") {
                Assert.False(ct.IsCancellationRequested); Assert.True(ct.CanBeCanceled); Deletes++;
                if (FailDelete) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
                Exists = false; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("/roles")) {
                if (CancelOnRole is not null) { CancelOnRole.Cancel(); throw new OperationCanceledException(ct); }
                if (FailRoleOnce) { FailRoleOnce = false; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); }
            }
            if (request.Method == HttpMethod.Get) return Json(new { id = "role-id", name = "admin" });
            var response = new HttpResponseMessage(HttpStatusCode.Created);
            if (path.EndsWith("/groups")) response.Headers.Location = new Uri("https://auth.test/admin/realms/tenant-test/groups/" + Guid.NewGuid());
            if (path.EndsWith("/users")) response.Headers.Location = new Uri("https://auth.test/admin/realms/tenant-test/users/owner");
            return Task.FromResult(response);
        }
        private static Task<HttpResponseMessage> Json(object value) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) });
    }
}
