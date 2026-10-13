using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.QualityCycle;
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

namespace CleaningSuite.Tests.QualityCycle;
public class QualityCycleAuthorizationTests
{
    [Theory]
    [InlineData("employee", 403)] [InlineData("manager", 403)] [InlineData("", 401)] [InlineData("admin", 200)]
    public async Task OnlyAuthenticatedTenantAdminCanManageTemplates(string role, int expected)
    {
        var repository = new Mock<IQualityCycleRepository>();
        repository.Setup(x => x.ListTemplatesAsync(null, null, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<QualityCycleTemplate>());
        var registry = new Mock<ITenantRegistry>();
        registry.Setup(x => x.GetBySlugAsync("tenant-a", It.IsAny<CancellationToken>())).ReturnsAsync(new TenantRegistration { Slug = "tenant-a", Status = TenantRegistration.StatusActive });
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:Keycloak:ServerUrl"] = "https://auth.test" }));
            builder.ConfigureTestServices(services => {
                foreach (var descriptor in services.Where(x => x.ImplementationType == typeof(MartenSchemaInitializer) || x.ImplementationType == typeof(QualityCycleWorker)).ToList()) services.Remove(descriptor);
                services.RemoveAll<IQualityCycleRepository>(); services.AddSingleton(repository.Object);
                services.RemoveAll<ITenantRegistry>(); services.AddSingleton(registry.Object);
                services.AddAuthentication(options => { options.DefaultAuthenticateScheme = "qc-test"; options.DefaultChallengeScheme = "qc-test"; options.DefaultForbidScheme = "qc-test"; })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthentication>("qc-test", _ => { });
            });
        });
        using var client = factory.CreateClient();
        if (role.Length > 0) client.DefaultRequestHeaders.Add("X-Test-Role", role);
        Assert.Equal((HttpStatusCode)expected, (await client.GetAsync("/api/v1/admin/quality-cycle/templates")).StatusCode);
        if (role != "admin") {
            foreach (var route in new[] { "/api/v1/admin/quality-cycle/monthly-summary?year=2026&month=10", "/api/v1/admin/quality-cycle/report-shares", "/api/v1/admin/quality-cycle/summary-pdf?year=2026&month=10&shiftId=" + Guid.NewGuid() })
                Assert.Equal((HttpStatusCode)expected, (await client.GetAsync(route)).StatusCode);
            Assert.Equal((HttpStatusCode)expected, (await client.PostAsync("/api/v1/admin/quality-cycle/report-shares", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"))).StatusCode);
        }
    }
    public class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
            var role = Request.Headers["X-Test-Role"].ToString();
            if (role.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(new[] { new Claim("iss", "https://auth.test/realms/tenant-a"), new Claim("realm_access", System.Text.Json.JsonSerializer.Serialize(new { roles = new[] { role } })) }, "qc-test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "qc-test")));
        }
    }
}
