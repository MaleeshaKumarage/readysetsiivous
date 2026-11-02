using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CleaningSuite.Api;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.Invoicing;
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

namespace CleaningSuite.Tests.Invoicing;

public class ManualInvoiceAuthorizationTests
{
    [Theory]
    [InlineData("", 401)] [InlineData("employee", 403)] [InlineData("manager", 403)] [InlineData("admin", 200)]
    public async Task Manual_routes_enforce_admin_policy_and_resolve_real_service(string role, int expected)
    {
        var f = new ManualInvoiceServiceTests.Fixture();
        var registry = new Mock<ITenantRegistry>();
        registry.Setup(x => x.GetBySlugAsync("tenant-a", It.IsAny<CancellationToken>())).ReturnsAsync(new TenantRegistration { Slug = "tenant-a", Status = TenantRegistration.StatusActive });
        var profiles = new Mock<ITenantProfileRepository>(); profiles.Setup(x => x.GetAsync("tenant-a", It.IsAny<CancellationToken>())).ReturnsAsync(f.Profile);
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:Keycloak:ServerUrl"] = "https://auth.test" }));
            builder.ConfigureTestServices(services => {
                foreach (var descriptor in services.Where(x => x.ImplementationType == typeof(MartenSchemaInitializer) || x.ImplementationType == typeof(QualityCycleWorker)).ToList()) services.Remove(descriptor);
                services.RemoveAll<IEmployeeRepository>(); services.AddSingleton(new Mock<IEmployeeRepository>().Object);
                services.RemoveAll<ITenantRegistry>(); services.AddSingleton(registry.Object);
                services.RemoveAll<IInvoiceRepository>(); services.AddSingleton(f.Invoices.Object);
                services.RemoveAll<ICompanyRepository>(); services.AddSingleton(f.Companies.Object);
                services.RemoveAll<ITenantProfileRepository>(); services.AddSingleton(profiles.Object);
                services.RemoveAll<IShiftWorkReservation>(); services.AddSingleton(f.Reservation.Object);
                services.AddAuthentication(options => { options.DefaultAuthenticateScheme = "qc-test"; options.DefaultChallengeScheme = "qc-test"; options.DefaultForbidScheme = "qc-test"; })
                    .AddScheme<AuthenticationSchemeOptions, QualityCycleAuthorizationTests.TestAuthentication>("qc-test", _ => { });
            });
        });
        using var client = factory.CreateClient(); if (role.Length > 0) client.DefaultRequestHeaders.Add("X-Test-Role", role);
        var preview = await client.PostAsJsonAsync("/api/v1/admin/invoices/manual-preview", f.Input);
        Assert.Equal((HttpStatusCode)expected, preview.StatusCode);
        if (role != "admin") {
            Assert.Equal((HttpStatusCode)expected, (await client.PostAsJsonAsync("/api/v1/admin/invoices/manual", f.Input)).StatusCode);
            Assert.Empty(f.Invoices.Invocations); Assert.Empty(f.Reservation.Invocations); return;
        }
        var body = await preview.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(125.5m, body.GetProperty("total").GetProperty("gross").GetDecimal());
        var input = f.Input with { PreviewToken = body.GetProperty("previewToken").GetString() };
        var issue = await client.PostAsJsonAsync("/api/v1/admin/invoices/manual", input); Assert.Equal(HttpStatusCode.Created, issue.StatusCode);
        var first = await issue.Content.ReadFromJsonAsync<JsonElement>();
        var retry = await client.PostAsJsonAsync("/api/v1/admin/invoices/manual", input); Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(first.GetProperty("id").GetGuid(), (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        Assert.Single(f.Saved);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/admin/invoices/manual-preview", f.Input with { Lines = [null!] })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/admin/invoices/manual", input with { Lines = [new("Other", 1, 50)] })).StatusCode);
    }
}
