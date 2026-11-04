using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Agreements;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Infrastructure.Persistence;
using CleaningSuite.Tests.QualityCycle;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Customers;

public class CustomerWorkspaceDatabaseTests
{
    [QualityCycleDatabaseFact]
    public async Task Customer_company_and_agreements_are_invisible_to_another_tenant()
    {
        var connection = Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCleaningMarten(connection, databaseSchemaName: "customer_ws_" + Guid.NewGuid().ToString("N"));
        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var tenantA = "customer-a-" + Guid.NewGuid().ToString("N");
        var tenantB = "customer-b-" + Guid.NewGuid().ToString("N");
        var company = Company.Create("1234567-8", "Synthetic customer");
        var agreement = new Agreement { Id = Guid.NewGuid(), CompanyId = company.Id, Title = "Synthetic agreement" };
        using (var seed = store.LightweightSession(tenantA))
        {
            seed.Store(company);
            seed.Store(agreement);
            await seed.SaveChangesAsync();
        }

        var agreements = new AgreementRepository(store);
        var companyOwner = new TenantSessionFactory(store, Tenant(tenantA).Object);
        var companyOther = new TenantSessionFactory(store, Tenant(tenantB).Object);
        Assert.Equal("Synthetic customer", (await new CompanyRepository(companyOwner).GetAsync(company.Id))!.Name);
        Assert.Null(await new CompanyRepository(companyOther).GetAsync(company.Id));
        Assert.Single(await agreements.ListAsync(tenantA));
        Assert.Empty(await agreements.ListAsync(tenantB));
    }

    private static Mock<ITenantContext> Tenant(string id)
    {
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(x => x.TenantId).Returns(id);
        return tenant;
    }
}
