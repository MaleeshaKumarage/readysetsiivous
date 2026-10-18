using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Invoicing;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Invoicing;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Domain.Tenants;
using CleaningSuite.Infrastructure.Persistence;
using CleaningSuite.Tests.QualityCycle;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CleaningSuite.Tests.Invoicing;

public class ShiftInvoiceConcurrencyDatabaseTests
{
    [QualityCycleDatabaseFact]
    public async Task Concurrent_issue_requests_persist_one_invoice_and_one_number_with_tenant_isolation()
    {
        var connection = Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!;
        var services = new ServiceCollection(); services.AddLogging();
        services.AddCleaningMarten(connection, databaseSchemaName: "shift_invoice_race_" + Guid.NewGuid().ToString("N"));
        using var provider = services.BuildServiceProvider(); var store = provider.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var partition = "invoice-" + Guid.NewGuid().ToString("N");
        var company = Company.Create("1234567-8", "Customer");
        var branch = Branch.Create(company.Id, "Branch");
        var shift = Shift.Create(company.Id, branch.Id, "Monthly shift", new() { Type = ShiftScheduleType.OnCallFlexible }, null, new(2026, 10, 1), null);
        shift.CreatedUtc = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var price = new ShiftBillingProfile { Id = shift.Id, ShiftId = shift.Id, Plans = [new("Monthly", 150, 25.5m, new(2026, 10, 1))] };
        using (var writer = store.LightweightSession(partition)) { writer.Store(price); await writer.SaveChangesAsync(); }
        var profile = new TenantProfile { CompanyName = "Sample supplier", BusinessId = "1234567-8", CompanyAddress = new() { Street = "Sample 1", City = "Helsinki", PostalCode = "00100" }, BankAccountIBAN = "FI2112345600000785", BankBic = "NDEAFIHH", InvoiceIssuerIsSample = true };
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = connection }).Build();
        var clock = new Mock<TimeProvider>(); clock.Setup(c => c.GetUtcNow()).Returns(new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.Zero));
        var shifts = new Mock<IShiftRepository>(); shifts.Setup(r => r.ListByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([shift]);
        shifts.Setup(r => r.ListOccurrenceExceptionsAsync(shift.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        shifts.Setup(r => r.ListAssignmentHistoryAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        shifts.Setup(r => r.ListOccurrenceAssignmentsAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var companies = new Mock<ICompanyRepository>(); companies.Setup(r => r.GetAsync(company.Id, It.IsAny<CancellationToken>())).ReturnsAsync(company);
        var branches = new Mock<IBranchRepository>(); branches.Setup(r => r.GetAsync(branch.Id, It.IsAny<CancellationToken>())).ReturnsAsync(branch);
        var forms = new Mock<IQualityCycleRepository>(); forms.Setup(r => r.ListFormsAsync(shift.Id, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var profiles = new Mock<ITenantProfileRepository>(); profiles.Setup(r => r.GetAsync(partition, It.IsAny<CancellationToken>())).ReturnsAsync(profile);
        TenantSessionFactory Session(string id) { var context = new Mock<ITenantContext>(); context.SetupGet(c => c.TenantId).Returns(id); return new(store, context.Object); }
        ShiftInvoiceService Service(TenantSessionFactory session)
        {
            var context = new Mock<ITenantContext>(); context.SetupGet(c => c.TenantId).Returns(partition);
            return new(shifts.Object, new ShiftBillingRepository(session), new InvoiceRepository(session), companies.Object, branches.Object,
                forms.Object, profiles.Object, context.Object, new ShiftWorkReservation(config, session), clock.Object);
        }
        using var firstSession = Session(partition); using var secondSession = Session(partition);
        var first = Service(firstSession); var second = Service(secondSession);
        var input = new ShiftInvoiceRequest(company.Id, [shift.Id], new(2026, 10, 1), new(2026, 10, 31), new() { Street = "Customer 2", PostalCode = "00100", City = "Helsinki" });
        var preview = await first.PreviewAsync(input, default);
        var otherPreview = await second.PreviewAsync(input, default);
        Assert.Equal(preview.PreviewToken, otherPreview.PreviewToken);
        async Task<Exception?> Issue(ShiftInvoiceService service, string token)
        { try { await service.IssueAsync(input with { PreviewToken = token }, default); return null; } catch (Exception error) { return error; } }
        var results = await Task.WhenAll(Issue(first, preview.PreviewToken), Issue(second, otherPreview.PreviewToken));
        Assert.Single(results, r => r is null);
        Assert.IsType<ShiftConflictException>(Assert.Single(results, r => r is not null));
        using var check = Session(partition);
        var saved = Assert.Single(await new InvoiceRepository(check).ListAsync(null, null));
        Assert.Equal(150, saved.Total.Net); Assert.Equal(37.88m, saved.Total.Vat);
        Assert.Equal("2026-0001", saved.InvoiceNumber); Assert.True(saved.IssuerIsSample);
        Assert.Equal(input.BillingAddress!.Street, saved.BillingAddress.Street);
        using var foreign = Session(partition + "-foreign");
        Assert.Empty(await new InvoiceRepository(foreign).ListAsync(null, null));
        Assert.Null(await new ShiftBillingRepository(foreign).GetAsync(shift.Id));
    }
}
