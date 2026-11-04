using CleaningSuite.Application.Agreements;
using CleaningSuite.Application.Agreements.Queries;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Invoicing;
using CleaningSuite.Application.Invoicing.Queries;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Agreements;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Invoicing;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Customers;

public class CustomerWorkspaceQueryTests
{
    private static Mock<ITenantContext> Tenant(string id)
    {
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(x => x.TenantId).Returns(id);
        return tenant;
    }

    [Fact]
    public async Task Agreement_summaries_filter_by_company_and_name_the_customer()
    {
        var customerA = Guid.NewGuid();
        var customerB = Guid.NewGuid();
        var missing = Guid.NewGuid();
        var agreements = new[]
        {
            new Agreement { Id = Guid.NewGuid(), CompanyId = customerA, Title = "A1" },
            new Agreement { Id = Guid.NewGuid(), CompanyId = customerB, Title = "B1" },
            new Agreement { Id = Guid.NewGuid(), CompanyId = missing, Title = "Orphan" },
        };
        var repository = new Mock<IAgreementRepository>();
        repository.Setup(x => x.ListAsync("tenant-a", It.IsAny<CancellationToken>())).ReturnsAsync(agreements);
        var companies = new Mock<ICompanyRepository>();
        companies.Setup(x => x.GetAsync(customerA, It.IsAny<CancellationToken>())).ReturnsAsync(Company.Create("1234567-8", "Customer A"));
        companies.Setup(x => x.GetAsync(missing, It.IsAny<CancellationToken>())).ReturnsAsync((Company?)null);
        var handler = new ListAgreementSummariesHandler(Tenant("tenant-a").Object, repository.Object, companies.Object);

        var scoped = await handler.Handle(new ListAgreementSummariesQuery(customerA), default);
        var only = Assert.Single(scoped);
        Assert.Equal("A1", only.Title);
        Assert.Equal("Customer A", only.CompanyName);

        var all = await handler.Handle(new ListAgreementSummariesQuery(), default);
        Assert.Equal(3, all.Count);
        Assert.Equal("Unknown customer", all.Single(a => a.Title == "Orphan").CompanyName);
    }

    [Fact]
    public async Task Agreement_summaries_read_only_the_current_tenant()
    {
        var repository = new Mock<IAgreementRepository>();
        repository.Setup(x => x.ListAsync("tenant-a", It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<Agreement>());
        var companies = new Mock<ICompanyRepository>();
        var handler = new ListAgreementSummariesHandler(Tenant("tenant-a").Object, repository.Object, companies.Object);

        Assert.Empty(await handler.Handle(new ListAgreementSummariesQuery(Guid.NewGuid()), default));
        repository.Verify(x => x.ListAsync("tenant-a", It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(x => x.ListAsync(It.Is<string>(t => t != "tenant-a"), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Invoice_list_filters_by_company_and_excludes_unlinked_invoices()
    {
        var customer = Guid.NewGuid();
        var invoices = new[]
        {
            new Invoice { Id = Guid.NewGuid(), CompanyId = customer },
            new Invoice { Id = Guid.NewGuid(), CompanyId = Guid.NewGuid() },
            new Invoice { Id = Guid.NewGuid(), CompanyId = null },
        };
        var repository = new Mock<IInvoiceRepository>();
        repository.Setup(x => x.ListAsync(null, null, It.IsAny<CancellationToken>())).ReturnsAsync(invoices);
        var handler = new ListInvoicesHandler(repository.Object);

        var scoped = await handler.Handle(new ListInvoicesQuery(null, null, customer), default);
        Assert.Equal(invoices[0].Id, Assert.Single(scoped).Id);

        var unfiltered = await handler.Handle(new ListInvoicesQuery(null, null), default);
        Assert.Equal(3, unfiltered.Count);
    }
}
