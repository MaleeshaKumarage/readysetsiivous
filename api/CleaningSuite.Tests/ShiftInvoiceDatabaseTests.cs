using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Invoicing;
using CleaningSuite.Infrastructure.Persistence;
using CleaningSuite.Tests.QualityCycle;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Moq;
namespace CleaningSuite.Tests.Invoicing;
public class ShiftInvoiceDatabaseTests
{
    [QualityCycleDatabaseFact]
    public async Task Pricing_and_invoice_snapshots_roundtrip_in_their_tenant_partition()
    {
        var services=new ServiceCollection();services.AddLogging();
        services.AddCleaningMarten(Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!,databaseSchemaName:"invoice_snapshot_"+Guid.NewGuid().ToString("N"));
        using var provider=services.BuildServiceProvider();var store=provider.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var partition="invoice-"+Guid.NewGuid().ToString("N");var shiftId=Guid.NewGuid();var invoiceId=Guid.NewGuid();
        TenantSessionFactory Session(string tenant) { var context=new Mock<ITenantContext>();context.SetupGet(x=>x.TenantId).Returns(tenant);return new(store,context.Object); }
        var visit=new DateTime(2026,10,2,8,0,0,DateTimeKind.Utc);
        using(var session=Session(partition))
        {
            var pricing=new ShiftBillingRepository(session);
            await pricing.SaveAsync(new(){Id=shiftId,ShiftId=shiftId,Plans=[new("Monthly",150,25.5m,new(2026,10,1))],Overrides=[new(visit,45,"Extra work")]});
            var invoices=new InvoiceRepository(session);
            await invoices.SaveAsync(new(){Id=invoiceId,InvoiceNumber="2026-0001",CompanyId=Guid.NewGuid(),PeriodFrom=new(2026,10,1),PeriodTo=new(2026,10,31),Language="sv",IssuerIsSample=true,PaymentReference="202600017",BillingAddress=new(){Street="Billing Street",PostalCode="00100",City="Helsinki"},CustomerBusinessId="1234567-8",Issuer=new(){CompanyName="Issuer snapshot",BankAccountIBAN="FI2112345600000785"},Lines=[new(){ShiftId=shiftId,ShiftName="Biweekly",BranchName="AAA1",Hours=4,Occurrences=2,NetAmount=150,UnitPriceNet=150,VatRatePercent=25.5m,VatAmount=38.25m,PricingMode="Monthly",PeriodFrom=new(2026,10,1),PeriodTo=new(2026,10,31),Month="2026-10",OverrideReason="Agreed"}],Total=new(){Net=150,Vat=38.25m}});
        }
        using(var session=Session(partition+"-other"))
        {
            Assert.Null(await new ShiftBillingRepository(session).GetAsync(shiftId));
            Assert.Null(await new InvoiceRepository(session).GetByIdAsync(invoiceId));
            Assert.Empty(await new InvoiceRepository(session).ListAsync(null,null));
        }
        using(var session=Session(partition))
        {
            var profile=(await new ShiftBillingRepository(session).GetAsync(shiftId))!;
            Assert.Equal(150,Assert.Single(profile.Plans).RateNet);Assert.Equal(visit,Assert.Single(profile.Overrides).OccurrenceStartUtc);
            var invoice=(await new InvoiceRepository(session).GetByIdAsync(invoiceId))!;
            Assert.Equal("sv",invoice.Language);Assert.True(invoice.IssuerIsSample);Assert.Equal("Billing Street",invoice.BillingAddress.Street);Assert.Equal("Issuer snapshot",invoice.Issuer.CompanyName);Assert.Equal("202600017",invoice.PaymentReference);
            Assert.Equal(new DateOnly(2026,10,31),invoice.PeriodTo);Assert.Equal(188.25m,invoice.Total.Gross);
            var line=Assert.Single(invoice.Lines);Assert.Equal(4,line.Hours);Assert.Equal(2,line.Occurrences);Assert.Equal(150,line.NetAmount);Assert.Equal("Agreed",line.OverrideReason);Assert.Equal("AAA1",line.BranchName);
        }
    }
}
