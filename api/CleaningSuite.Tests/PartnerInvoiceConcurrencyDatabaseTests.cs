using CleaningSuite.Application.Invoicing;
using CleaningSuite.Application.Partners;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Common;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Invoicing;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Tenants;
using CleaningSuite.Infrastructure.Partners;
using CleaningSuite.Infrastructure.Persistence;
using CleaningSuite.Tests.QualityCycle;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
namespace CleaningSuite.Tests.Invoicing;

[Collection("Database integration")]
public class PartnerInvoiceConcurrencyDatabaseTests
{
    [QualityCycleDatabaseFact]
    public async Task Concurrent_month_issue_cannot_bill_same_partner_visit_twice()
    {
        var connection=Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!;
        var services=new ServiceCollection();services.AddLogging();services.AddTestMarten(connection,databaseSchemaName:"partner_invoice_"+Guid.NewGuid().ToString("N"));
        using var provider=services.BuildServiceProvider();var store=provider.GetRequiredService<IDocumentStore>();await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var tenantId="partner-invoice-"+Guid.NewGuid().ToString("N");var tenant=new Mock<ITenantContext>();tenant.SetupGet(t=>t.TenantId).Returns(tenantId);
        var company=Company.Create("1234567-8","Synthetic owner");company.IsPartnerEnabled=true;
        var branch=Branch.Create(company.Id,"Apartment");branch.SetPricing(100,25.5m,120);branch.SetSizeM2(34);
        var partner=new Employee{Role=Employee.RolePartner,PartnerCompanyId=company.Id};var admin=new Employee{Role=Employee.RoleAdmin};var cleaner=new Employee{Role=Employee.RoleEmployee};
        using(var seed=store.LightweightSession(tenantId)){seed.Store(company);seed.Store(branch);seed.Store(partner,admin,cleaner);await seed.SaveChangesAsync();}
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",connection}}).Build();
        var profiles=new Mock<ITenantProfileRepository>();profiles.Setup(p=>p.GetAsync(tenantId,It.IsAny<CancellationToken>())).ReturnsAsync(new TenantProfile{TimeZoneId="Europe/Helsinki",CompanyName="Sample supplier",BusinessId="1234567-8",CompanyAddress=new(){Street="Sample 1",City="Helsinki",PostalCode="00100"},BankAccountIBAN="FI2112345600000785",BankBic="NDEAFIHH",InvoiceIssuerIsSample=true});
        Guid shiftId;
        using(var session=new TenantSessionFactory(store,tenant.Object))
        {
            var workflow=new PartnerService(new PartnerStore(session),new ShiftWorkReservation(config,session),new ShiftRepository(session),profiles.Object,tenant.Object);
            var request=await workflow.Create(partner.Id,new(Guid.NewGuid(),branch.Id,new(2026,11,10),new(2026,11,10),"08:00"),default);
            var offered=await workflow.Offer(admin.Id,request.Id,new(request.Version,cleaner.Id),default);
            var accepted=await workflow.Accept(cleaner.Id,offered.OfferId!.Value,new(offered.Version),default);shiftId=accepted.LinkedShiftId!.Value;
            var form=new QualityCycleForm{ShiftId=shiftId,CompanyId=company.Id,BranchId=branch.Id,EmployeeId=cleaner.Id,ShiftOccurrenceUtc=accepted.PlannedStartUtc!.Value,ShiftOccurrenceEndUtc=accepted.PlannedEndUtc!.Value,IsSubmitted=true,StartedAtUtc=accepted.PlannedStartUtc,EndedAtUtc=accepted.PlannedEndUtc};
            session.Session.Store(form);await session.Session.SaveChangesAsync();
        }
        ShiftInvoiceService Service(TenantSessionFactory session)=>new(new ShiftRepository(session),new ShiftBillingRepository(session),new InvoiceRepository(session),new CompanyRepository(session),new BranchRepository(session),new QualityCycleRepository(session,store),profiles.Object,tenant.Object,new ShiftWorkReservation(config,session),TimeProvider.System,new PartnerInvoiceSource(new PartnerStore(session)));
        using var firstSession=new TenantSessionFactory(store,tenant.Object);using var secondSession=new TenantSessionFactory(store,tenant.Object);
        var first=Service(firstSession);var second=Service(secondSession);
        var input=new ShiftInvoiceRequest(company.Id,[],new(2026,11,1),new(2026,11,30),new Address{Street="Customer 1",PostalCode="00100",City="Helsinki"},PartnerMonth:true);
        var firstPreview=await first.PreviewAsync(input,default);var secondPreview=await second.PreviewAsync(input,default);Assert.Equal(firstPreview.PreviewToken,secondPreview.PreviewToken);
        async Task<Exception?> Issue(ShiftInvoiceService service,string token){try{await service.IssueAsync(input with{PreviewToken=token},default);return null;}catch(Exception error){return error;}}
        var results=await Task.WhenAll(Issue(first,firstPreview.PreviewToken),Issue(second,secondPreview.PreviewToken));
        Assert.Single(results,r=>r is null);Assert.Single(results,r=>r is not null);
        using var check=store.QuerySession(tenantId);var invoice=Assert.Single(await check.Query<Invoice>().ToListAsync());Assert.Equal(100,invoice.Total.Net);Assert.Equal(25.5m,invoice.Total.Vat);Assert.Equal(shiftId,Assert.Single(invoice.Lines).ShiftId);
        using var foreign=store.QuerySession(tenantId+"-foreign");Assert.Empty(await foreign.Query<Invoice>().ToListAsync());
    }
}
