using System.Net;
using System.Net.Http.Json;
using CleaningSuite.Api;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Tenants;
using CleaningSuite.Tests.QualityCycle;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace CleaningSuite.Tests.Shifts;

/// <summary>Branch visits are managed by administrators and managers only. Cleaners and partners are refused before any data is read.</summary>
public class BranchVisitAuthorizationTests
{
    [Theory]
    [InlineData("", 401)] [InlineData("employee", 403)] [InlineData("partner", 403)]
    public async Task Branch_visit_routes_refuse_everyone_but_administrators_and_managers(string role, int expected)
    {
        var registry = new Mock<ITenantRegistry>();
        registry.Setup(x => x.GetBySlugAsync("tenant-a", It.IsAny<CancellationToken>())).ReturnsAsync(new TenantRegistration { Slug = "tenant-a", Status = TenantRegistration.StatusActive });
        var reservation = new Mock<IShiftWorkReservation>();
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:Keycloak:ServerUrl"] = "https://auth.test" }));
            builder.ConfigureTestServices(services => {
                foreach (var descriptor in services.Where(x => x.ImplementationType == typeof(MartenSchemaInitializer) || x.ImplementationType == typeof(QualityCycleWorker)).ToList()) services.Remove(descriptor);
                services.RemoveAll<IEmployeeRepository>(); services.AddSingleton(new Mock<IEmployeeRepository>().Object);
                services.RemoveAll<ITenantRegistry>(); services.AddSingleton(registry.Object);
                services.RemoveAll<IShiftWorkReservation>(); services.AddSingleton(reservation.Object);
                services.AddAuthentication(options => { options.DefaultAuthenticateScheme = "qc-test"; options.DefaultChallengeScheme = "qc-test"; options.DefaultForbidScheme = "qc-test"; })
                    .AddScheme<AuthenticationSchemeOptions, QualityCycleAuthorizationTests.TestAuthentication>("qc-test", _ => { });
            });
        });
        using var client = factory.CreateClient(); if (role.Length > 0) client.DefaultRequestHeaders.Add("X-Test-Role", role);
        var shift = Guid.NewGuid(); var visit = Guid.NewGuid();
        var body = new AddBranchVisit(Guid.NewGuid(), new(2026, 11, 10), new(2026, 11, 10), "08:00", "10:00");
        foreach (var area in new[] { "admin", "manager" })
        {
            var root = $"/api/v1/{area}/branch-visits";
            Assert.Equal((HttpStatusCode)expected, (await client.GetAsync($"{root}?branchId={Guid.NewGuid()}&from=2026-11-01&to=2026-12-01")).StatusCode);
            Assert.Equal((HttpStatusCode)expected, (await client.PostAsJsonAsync(root, body)).StatusCode);
            Assert.Equal((HttpStatusCode)expected, (await client.PutAsJsonAsync($"{root}/shifts/{shift}/default-employee", new SetDefaultEmployee(Guid.NewGuid()))).StatusCode);
            Assert.Equal((HttpStatusCode)expected, (await client.PutAsJsonAsync($"{root}/shifts/{shift}/visits/{visit}/employee", new SetVisitEmployee(Guid.NewGuid(), 0))).StatusCode);
        }
        // Refused before the work reservation or any document is touched.
        Assert.Empty(reservation.Invocations);
    }
}
