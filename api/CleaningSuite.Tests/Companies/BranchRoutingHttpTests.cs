using System.Net;
using System.Text;
using System.Text.Json;
using CleaningSuite.Api;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Tenants;
using CleaningSuite.Tests.Employees;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Moq;

namespace CleaningSuite.Tests.Companies;

public sealed class BranchRoutingHttpTests
{
    [Theory]
    [InlineData("", false)]
    [InlineData(",\"preferredCleanerEmployeeId\":null,\"sizeSquareMeters\":null", true)]
    public async Task Branch_update_preserves_omitted_routing_and_size_but_explicit_null_clears(string extra, bool cleared)
    {
        using var host = new Host();
        var response = await host.Client.PutAsync($"/api/v1/admin/companies/branches/{host.Branch.Id}",
            new StringContent("{\"name\":\"Property\"" + extra + "}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (cleared)
        { Assert.Null(host.Branch.PreferredCleanerEmployeeId); Assert.Null(host.Branch.SizeSquareMeters); }
        else
        { Assert.Equal(host.Cleaner.Id, host.Branch.PreferredCleanerEmployeeId); Assert.Equal(75m, host.Branch.SizeSquareMeters); }
        Assert.False(json.RootElement.TryGetProperty("hasPreferredCleanerEmployeeId", out _));
    }

    [Theory]
    [InlineData(true, "Street 1, 00100 Helsinki")]
    [InlineData(false, "Manual title")]
    public async Task Only_partner_company_branch_updates_generate_address_names(bool partner, string expected)
    {
        using var host = new Host(partner);
        var response = await host.Client.PutAsync($"/api/v1/admin/companies/branches/{host.Branch.Id}",
            new StringContent("{\"name\":\"Manual title\",\"street\":\"Street 1\",\"postalCode\":\"00100\",\"city\":\"Helsinki\"}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, host.Branch.Name);
    }

    [Fact]
    public async Task Existing_name_only_partner_property_keeps_legacy_name_when_size_and_price_change()
    {
        using var host = new Host(true);
        var response = await host.Client.PutAsync($"/api/v1/admin/companies/branches/{host.Branch.Id}",
            new StringContent("{\"name\":\"Ignored manual rename\",\"sizeSquareMeters\":80,\"priceNet\":90,\"vatRatePercent\":25.5,\"estimatedDurationMinutes\":120}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Property", host.Branch.Name); Assert.Equal(80m, host.Branch.SizeSquareMeters);
        Assert.Equal(90m, host.Branch.PriceNet); Assert.True(host.Branch.PricingReady);
        Assert.Null(host.Branch.Street); Assert.Null(host.Branch.PostalCode); Assert.Null(host.Branch.City);
    }

    [Fact]
    public async Task Clearing_all_address_components_of_an_addressed_partner_property_is_rejected()
    {
        using var host = new Host(true); host.Branch.Street = "Existing street";
        var response = await host.Client.PutAsync($"/api/v1/admin/companies/branches/{host.Branch.Id}",
            new StringContent("{\"name\":\"Manual fallback\",\"street\":null,\"postalCode\":null,\"city\":null}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Omitted_preferred_cleaner_survives_deactivation_during_unrelated_update()
    {
        using var host = new Host(); host.Cleaner.IsActive = false;
        var response = await host.Client.PutAsync($"/api/v1/admin/companies/branches/{host.Branch.Id}",
            new StringContent("{\"name\":\"New title\",\"sizeSquareMeters\":80}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(host.Cleaner.Id, host.Branch.PreferredCleanerEmployeeId); Assert.Equal(80m, host.Branch.SizeSquareMeters);
    }

    [Fact]
    public async Task Ordinary_company_branch_blank_name_is_a_validation_error()
    {
        using var host = new Host();
        var response = await host.Client.PutAsync($"/api/v1/admin/companies/branches/{host.Branch.Id}",
            new StringContent("{\"name\":\" \"}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Foreign_or_unlinked_preferred_cleaner_is_rejected()
    {
        using var host = new Host();
        var response = await host.Client.PutAsync($"/api/v1/admin/companies/branches/{host.Branch.Id}",
            new StringContent("{\"name\":\"Property\",\"preferredCleanerEmployeeId\":\"" + Guid.NewGuid() + "\"}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(host.Cleaner.Id, host.Branch.PreferredCleanerEmployeeId);
    }

    [Fact]
    public void Property_size_and_generated_partner_name_have_explicit_validation()
    {
        var branch = Branch.Create(Guid.NewGuid(), "Manual");
        Assert.Throws<ArgumentException>(() => branch.SetSize(0));
        Assert.Throws<ArgumentException>(() => branch.SetSize(12.345m));
        branch.SetSize(75.25m); Assert.Equal(75.25m, branch.SizeSquareMeters);
        branch.SetSize(null); Assert.Null(branch.SizeSquareMeters);
        Assert.Equal("Street 1, 00100 Helsinki", Branch.PartnerPropertyName(" Street 1 ", "00100", "Helsinki"));
        Assert.Throws<ArgumentException>(() => Branch.PartnerPropertyName(null, null, null));
    }

    private sealed class Host : IDisposable
    {
        private readonly WebApplicationFactory<Program> factory;
        public Employee Admin { get; } = new() { KeycloakUserId = "employee-sub", Role = Employee.RoleAdmin };
        public Employee Cleaner { get; } = new() { KeycloakUserId = "cleaner-sub" };
        public Branch Branch { get; }
        public HttpClient Client { get; }
        public Host(bool partner = false)
        {
            Branch = Branch.Create(Guid.NewGuid(), "Property"); Branch.PreferredCleanerEmployeeId = Cleaner.Id; Branch.SetSize(75);
            var people = new Mock<IEmployeeRepository>();
            people.Setup(x => x.GetByKeycloakUserIdAsync("employee-sub", It.IsAny<CancellationToken>())).ReturnsAsync(Admin);
            people.Setup(x => x.GetByIdAsync(Cleaner.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Cleaner);
            people.Setup(x => x.ReloadAsync(Admin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Admin);
            var branches = new Mock<IBranchRepository>();
            branches.Setup(x => x.GetAsync(Branch.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Branch);
            var companies = new Mock<ICompanyRepository>();
            var company = Company.Create("SYNTHETIC", "Company"); company.IsPartnerEnabled = partner;
            companies.Setup(x => x.GetAsync(Branch.CompanyId, It.IsAny<CancellationToken>())).ReturnsAsync(company);
            var reservation = new Mock<IShiftWorkReservation>();
            reservation.Setup(x => x.AcquireAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new Lease());
            var registry = new Mock<ITenantRegistry>();
            registry.Setup(x => x.GetBySlugAsync("tenant-a", It.IsAny<CancellationToken>())).ReturnsAsync(new TenantRegistration { Slug = "tenant-a", Status = TenantRegistration.StatusActive });
            factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:Keycloak:ServerUrl"] = "https://auth.test" }));
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IHostedService>();
                    services.RemoveAll<IEmployeeRepository>(); services.AddSingleton(people.Object);
                    services.RemoveAll<IBranchRepository>(); services.AddSingleton(branches.Object);
                    services.RemoveAll<ICompanyRepository>(); services.AddSingleton(companies.Object);
                    services.RemoveAll<IShiftWorkReservation>(); services.AddSingleton(reservation.Object);
                    services.RemoveAll<ITenantRegistry>(); services.AddSingleton(registry.Object);
                    services.AddAuthentication(options => { options.DefaultAuthenticateScheme = "employee-test"; options.DefaultChallengeScheme = "employee-test"; })
                        .AddScheme<AuthenticationSchemeOptions, EmployeeAccessTests.TestAuthentication>("employee-test", _ => { });
                });
            });
            Client = factory.CreateClient(); Client.DefaultRequestHeaders.Add("X-Test-Role", "admin"); Client.DefaultRequestHeaders.Add("X-Test-Verified", "true");
        }
        public void Dispose() { Client.Dispose(); factory.Dispose(); }
        private sealed class Lease : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    }
}
