using System.Net;
using System.Net.Http.Json;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Infrastructure.Keycloak;
using CleaningSuite.Infrastructure.Leads;
using Microsoft.Extensions.Configuration;
using Moq;

namespace CleaningSuite.Tests.Leads;
public class LeadAdminDirectoryGroupTests
{
    [Fact]
    public async Task InheritedOrdinaryOwnerVisibleWhileProtectedServiceAndInactiveAdminsAreExcluded()
    {
        var repository = new Mock<IEmployeeRepository>();
        repository.Setup(r => r.GetByKeycloakUserIdAsync("inactive", It.IsAny<CancellationToken>())).ReturnsAsync(new Employee { IsActive = false, Role = "admin" });
        repository.Setup(r => r.GetByKeycloakUserIdAsync("pending", It.IsAny<CancellationToken>())).ReturnsAsync(new Employee { IdentitySyncPending = true, Role = "admin" });
        var tenant = new Mock<ITenantContext>(); tenant.SetupGet(t => t.TenantId).Returns("tenant-a");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["Auth:Keycloak:ServerUrl"] = "https://auth.invalid",
            ["Auth:Keycloak:TenantAccounts:tenant-a:ClientId"] = "employee-runtime",
            ["Auth:Keycloak:TenantAccounts:tenant-a:ClientSecret"] = "synthetic-only"
        }).Build();
        var result = await new LeadAdminDirectory(new TenantKeycloakClients(new HttpClient(new DirectoryHandler()), config), repository.Object, tenant.Object).ListAsync(default);
        Assert.Equal("owner", Assert.Single(result).Id);
    }
    private sealed class DirectoryHandler : HttpMessageHandler
    {
        private static object Group(string name) => new { id = name, name, path = "/" + name };
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            object body;
            if (path.EndsWith("/token")) body = new { access_token = "synthetic", expires_in = 3600 };
            else if (path.Contains("group-by-path")) body = Group(path.Split('/').Last());
            else if (path.EndsWith("/groups" ) && !path.Contains("/users/")) body = new[] { Group("managed-users"), Group("app-employee"), Group("app-manager"), Group("app-admin") };
            else if (path.EndsWith("/role-mappings")) body = new { realmMappings = Array.Empty<object>() };
            else if (path.EndsWith("/realm/composite")) {
                var name = path.Split("/groups/", 2)[1].Split('/')[0];
                body = name == "managed-users" ? Array.Empty<object>() : new object[] { new { id = name, name = name[4..], composite = false } };
            }
            else if (path.EndsWith("/members")) body = new[] { "owner", "service", "protected", "inactive", "pending", "employee" }.Select(id => new { id, email = id + "@example.invalid", enabled = true, serviceAccountClientId = id == "service" ? "runtime" : "" }).ToArray();
            else if (path.Contains("/users/") && path.EndsWith("/groups")) {
                var id = path.Split("/users/", 2)[1].Split('/')[0];
                body = id == "protected" ? new[] { Group("managed-users"), Group("app-admin"), Group("protected") }
                    : id == "employee" ? new[] { Group("managed-users"), Group("app-employee") } : new[] { Group("managed-users"), Group("app-admin") };
            }
            else throw new InvalidOperationException(path);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) });
        }
    }
}
