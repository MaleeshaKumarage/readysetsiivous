using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CleaningSuite.Infrastructure.Keycloak;

namespace CleaningSuite.Tests.Auth;

public sealed class KeycloakExtensionTransportTests
{
    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task MissingOrDeniedExtensionNeverFallsBackToAdminRest(HttpStatusCode status)
    {
        var handler = new Transport { Status = status };
        var client = Scoped(handler);
        var error = await Assert.ThrowsAsync<KeycloakException>(() => client.FindUsersByEmailAsync("tenant-a", "person@example.invalid"));
        Assert.Equal(status, error.StatusCode);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("/realms/tenant-a/cleaning-identity/v1/users?email=person%40example.invalid&exact=true", handler.Requests[1].Path);
        Assert.DoesNotContain(handler.Requests, r => r.Path.StartsWith("/admin/"));
    }

    [Fact]
    public async Task CreationUsesExtensionLocationAndProfileUpdatesContainOnlyExplicitPatch()
    {
        var handler = new Transport();
        var client = Scoped(handler);
        Assert.Equal("created-user", await client.CreateUserAsync("tenant-a", new { username = "person@example.invalid" }));
        await client.UpdateUserAsync("tenant-a", "created-user", new { firstName = "Updated", enabled = false });
        var create = Assert.Single(handler.Requests.Where(r => r.Method == HttpMethod.Post && r.Path.EndsWith("/users")));
        Assert.Equal("/realms/tenant-a/cleaning-identity/v1/users", create.Path);
        var update = Assert.Single(handler.Requests.Where(r => r.Method == HttpMethod.Put));
        using var payload = JsonDocument.Parse(update.Body);
        Assert.Equal(new[] { "firstName", "enabled" }, payload.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Get);
    }

    [Fact]
    public async Task RealmPreflightAndPlatformAdministrativeRequestsUseSeparateSurfaces()
    {
        var handler = new Transport();
        await Scoped(handler).GetRealmAsync("tenant-a");
        var platform = new KeycloakAdminClient(new HttpClient(handler), "https://auth.invalid", "master", "platform", "synthetic");
        await platform.GetRealmAsync("tenant-a");
        Assert.Contains(handler.Requests, r => r.Path == "/realms/tenant-a/cleaning-identity/v1");
        Assert.Contains(handler.Requests, r => r.Path == "/admin/realms/tenant-a");
    }

    private static KeycloakAdminClient Scoped(Transport handler) => new(new HttpClient(handler), "https://auth.invalid", "tenant-a", "runtime", "synthetic", "tenant-a");
    private sealed class Transport : HttpMessageHandler
    {
        public HttpStatusCode Status = HttpStatusCode.OK;
        public List<(HttpMethod Method, string Path, string Body)> Requests = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.PathAndQuery;
            Requests.Add((request.Method, path, request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
            if (path.EndsWith("/token"))
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { access_token = "synthetic-token", expires_in = 300 }) };
            if (Status != HttpStatusCode.OK) return new(Status);
            if (request.Method == HttpMethod.Post && path.EndsWith("/users")) {
                var created = new HttpResponseMessage(HttpStatusCode.Created);
                created.Headers.Location = new Uri("https://auth.invalid/realms/tenant-a/cleaning-identity/v1/users/created-user");
                return created;
            }
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { }) };
        }
    }
}
