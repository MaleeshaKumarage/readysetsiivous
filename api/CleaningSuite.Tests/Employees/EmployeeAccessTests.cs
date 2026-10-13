using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Tenants;
using CleaningSuite.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace CleaningSuite.Tests.Employees;
public class EmployeeAccessTests
{
    [Theory]
    [InlineData("employee", "employee", true, true, false, 200)]
    [InlineData("admin", "admin", true, true, false, 200)]
    [InlineData("admin", "employee", true, true, false, 403)]
    [InlineData("employee", "admin", true, true, false, 403)]
    [InlineData("admin", "admin", false, true, false, 403)]
    [InlineData("employee", "employee", false, true, false, 403)]
    [InlineData("employee", "employee", true, false, false, 403)]
    [InlineData("employee", null, true, true, false, 403)]
    [InlineData("admin", null, true, true, false, 200)]
    [InlineData("employee", "employee", true, true, true, 403)]
    public async Task BoundIdentityGuardsCurrentEmployeeStateEvenWhenJwtStillHasOldRoles(string tokenRole, string? documentRole, bool active, bool verified, bool pending, int expected)
    {
        var repository = new Mock<IEmployeeRepository>();
        var employee = documentRole is null ? null : new Employee { KeycloakUserId = "employee-sub", Role = documentRole, IsActive = active, IdentitySyncPending = pending };
        repository.Setup(x => x.GetByKeycloakUserIdAsync("employee-sub", It.IsAny<CancellationToken>())).ReturnsAsync(employee);
        var registry = new Mock<ITenantRegistry>();
        registry.Setup(x => x.GetBySlugAsync("tenant-a", It.IsAny<CancellationToken>())).ReturnsAsync(new TenantRegistration { Slug = "tenant-a", Status = TenantRegistration.StatusActive });
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:Keycloak:ServerUrl"] = "https://auth.test" }));
            builder.ConfigureTestServices(services => {
                foreach (var descriptor in services.Where(x => x.ImplementationType == typeof(MartenSchemaInitializer) || x.ImplementationType == typeof(QualityCycleWorker)).ToList()) services.Remove(descriptor);
                services.RemoveAll<IEmployeeRepository>(); services.AddSingleton(repository.Object);
                services.RemoveAll<ITenantRegistry>(); services.AddSingleton(registry.Object);
                services.AddAuthentication(options => { options.DefaultAuthenticateScheme = "employee-test"; options.DefaultChallengeScheme = "employee-test"; })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthentication>("employee-test", _ => { });
            });
        });
        using var client = factory.CreateClient(); client.DefaultRequestHeaders.Add("X-Test-Role", tokenRole); client.DefaultRequestHeaders.Add("X-Test-Verified", verified.ToString());
        var response = await client.GetAsync("/api/v1/me");
        Assert.Equal((HttpStatusCode)expected, response.StatusCode);
        if (expected == 200 && employee is not null) Assert.Contains(employee.Id.ToString(), await response.Content.ReadAsStringAsync());
    }
    public class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
            var role = Request.Headers["X-Test-Role"].ToString();
            var identity = new ClaimsIdentity(new[] { new Claim("sub", "employee-sub"), new Claim("iss", "https://auth.test/realms/tenant-a"),
                new Claim("email_verified", Request.Headers["X-Test-Verified"].ToString()), new Claim("realm_access", System.Text.Json.JsonSerializer.Serialize(new { roles = new[] { role } })) }, "employee-test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "employee-test")));
        }
    }
}
