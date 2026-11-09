using CleaningSuite.Application.Partners;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Tenants;
using CleaningSuite.Infrastructure.Partners;
using CleaningSuite.Infrastructure.Persistence;
using CleaningSuite.Tests.QualityCycle;
using FluentValidation;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CleaningSuite.Tests.Partners;

[Collection("Database integration")]
public class PartnerRequestValidationDatabaseTests
{
    [QualityCycleDatabaseFact]
    public async Task An_empty_partner_request_names_every_missing_field()
    {
        var connection = Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!;
        var services = new ServiceCollection(); services.AddLogging();
        services.AddTestMarten(connection, databaseSchemaName: "partner_validation_" + Guid.NewGuid().ToString("N"));
        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IDocumentStore>(); await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var tenantId = "partner-validation-" + Guid.NewGuid().ToString("N");
        var tenant = new Mock<ITenantContext>(); tenant.SetupGet(t => t.TenantId).Returns(tenantId);
        var company = Company.Create("1234567-8", "Synthetic owner"); company.IsPartnerEnabled = true;
        var partner = new Employee { Role = Employee.RolePartner, PartnerCompanyId = company.Id };
        using (var seed = store.LightweightSession(tenantId)) { seed.Store(company); seed.Store(partner); await seed.SaveChangesAsync(); }
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { { "ConnectionStrings:DefaultConnection", connection } }).Build();
        var profiles = new Mock<ITenantProfileRepository>();
        using var session = new TenantSessionFactory(store, tenant.Object);
        var service = new PartnerService(new PartnerStore(session), new ShiftWorkReservation(config, session), new ShiftRepository(session), profiles.Object, tenant.Object);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => service.Create(partner.Id, new(Guid.Empty, Guid.Empty, default, default), default));
        var fields = ex.Errors.Select(e => e.PropertyName).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "BranchId", "ClientRequestId", "FromDate", "ToDate" }, fields);
        Assert.All(ex.Errors, e => Assert.False(string.IsNullOrWhiteSpace(e.ErrorMessage)));
    }
}
