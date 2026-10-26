using System.Net;
using CleaningSuite.Infrastructure.Keycloak;

namespace CleaningSuite.Tests.Employees;
public sealed class KeycloakEmployeeGroupClientTests
{
    [Fact]
    public async Task EffectiveRolesAndMembershipUsePinnedEndpoints()
    {
        var handler = new Transport();
        var client = new KeycloakAdminClient(new HttpClient(handler), "https://auth.invalid", "tenant-a", "runtime", "synthetic", "tenant-a");
        await client.GetEffectiveUserRealmRolesAsync("tenant-a", "user-a");
        await client.AddUserToGroupAsync("tenant-a", "user-a", "employee-group");
        await client.RemoveUserFromGroupAsync("tenant-a", "user-a", "admin-group");
        Assert.Contains("GET /admin/realms/tenant-a/users/user-a/role-mappings/realm/composite", handler.Paths);
        Assert.Contains("PUT /admin/realms/tenant-a/users/user-a/groups/employee-group", handler.Paths);
        Assert.Contains("DELETE /admin/realms/tenant-a/users/user-a/groups/admin-group", handler.Paths);
    }
    [Fact]
    public async Task TenantClientCannotConfigureGroupsOrPermissionsOrCrossRealms()
    {
        var handler = new Transport();
        var client = new KeycloakAdminClient(new HttpClient(handler), "https://auth.invalid", "tenant-a", "runtime", "synthetic", "tenant-a");
        await Assert.ThrowsAsync<KeycloakException>(() => client.CreateGroupAsync("tenant-a", new {name="forbidden"}));
        await Assert.ThrowsAsync<KeycloakException>(() => client.AssignGroupRealmRolesAsync("tenant-a", "employee", Array.Empty<object>()));
        await Assert.ThrowsAsync<KeycloakException>(() => client.CreateAuthorizationUserPolicyAsync("tenant-a", "permissions", new {}));
        await Assert.ThrowsAsync<KeycloakException>(() => client.AddUserToGroupAsync("tenant-b", "user", "group"));
        Assert.Empty(handler.Paths);
    }
    [Theory]
    [InlineData("/managed-users")] [InlineData("../protected")] [InlineData("protected")]
    [InlineData("app-admin/child")] [InlineData("%2fapp-admin")]
    public async Task FixedGroupLookupRejectsCallerPathsBeforeNetwork(string name)
    {
        var handler = new Transport();
        var client = new KeycloakAdminClient(new HttpClient(handler), "https://auth.invalid", "tenant-a", "runtime", "synthetic", "tenant-a");
        await Assert.ThrowsAsync<KeycloakException>(() => client.GetEmployeeGroupAsync("tenant-a", name));
        Assert.Empty(handler.Paths);
    }
    private sealed class Transport : HttpMessageHandler
    {
        public List<string> Paths { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/token"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"synthetic\",\"expires_in\":3600}") });
            Paths.Add(request.Method + " " + request.RequestUri.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
        }
    }
}
