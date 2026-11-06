using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Invoicing;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Invoicing;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Domain.Tenants;
using CleaningSuite.Infrastructure.Persistence;
using CleaningSuite.Tests.QualityCycle;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CleaningSuite.Tests.Invoicing;

public class ManualInvoiceDatabaseTests
{
    [QualityCycleDatabaseFact]
    public async Task Concurrent_retries_persist_one_snapshot_and_number_and_submission_lookup_is_tenant_scoped()
    {
        var connection = Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!;
        var services = new ServiceCollection(); services.AddLogging();
        services.AddCleaningMarten(connection, databaseSchemaName: "manual_invoice_race_" + Guid.NewGuid().ToString("N"));
        using var provider = services.BuildServiceProvider(); var store = provider.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var partition = "manual-" + Guid.NewGuid().ToString("N"); var company = Company.Create("1234567-8", "Customer");
        var profile = new TenantProfile { CompanyName = "Supplier", BusinessId = "1234567-8", TimeZoneId = "Europe/Helsinki", DefaultVatRatePercent = 25.5m,
            CompanyAddress = new() { Street = "Supplier 1", City = "Helsinki", PostalCode = "00100", Country = "FI" },
            BankAccountIBAN = "FI2112345600000785", BankBic = "NDEAFIHH", PaymentTermsDays = 14 };
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = connection }).Build();
        TenantSessionFactory Session(string id) { var context = new Mock<ITenantContext>(); context.SetupGet(c => c.TenantId).Returns(id); return new(store, context.Object); }
        ManualInvoiceService Service(TenantSessionFactory session, string tenantId)
        {
            var companies = new Mock<ICompanyRepository>(); companies.Setup(r => r.GetAsync(company.Id, It.IsAny<CancellationToken>())).ReturnsAsync(company);
            var profiles = new Mock<ITenantProfileRepository>(); profiles.Setup(r => r.GetAsync(tenantId, It.IsAny<CancellationToken>())).ReturnsAsync(profile);
            var tenant = new Mock<ITenantContext>(); tenant.SetupGet(c => c.TenantId).Returns(tenantId);
            var clock = new Mock<TimeProvider>(); clock.Setup(c => c.GetUtcNow()).Returns(new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero));
            return new(new InvoiceRepository(session), companies.Object, profiles.Object, tenant.Object, new ShiftWorkReservation(config, session), clock.Object);
        }
        using var firstSession = Session(partition); using var secondSession = Session(partition);
        var first = Service(firstSession, partition); var second = Service(secondSession, partition);
        var input = new ManualInvoiceRequest(company.Id, Guid.NewGuid(), [new("Agreed service", 1.5m, 50), new("Materials", 2, 10)],
            new() { Street = "Customer 2", PostalCode = "00100", City = "Helsinki", Country = "FI" });
        var preview = await first.PreviewAsync(input, default); var other = await second.PreviewAsync(input, default);
        var results = await Task.WhenAll(first.IssueAsync(input with { PreviewToken = preview.PreviewToken }, default),
            second.IssueAsync(input with { PreviewToken = other.PreviewToken }, default));
        Assert.Equal(results[0].Id, results[1].Id); Assert.Equal("2026-0001", results[0].InvoiceNumber);
        using (var check = Session(partition))
        {
            var repository = new InvoiceRepository(check); var saved = Assert.Single(await repository.ListAsync(null, null));
            Assert.Equal(saved.Id, (await repository.GetByManualSubmissionIdAsync(input.SubmissionId))!.Id);
            Assert.NotNull(saved.ManualRequestFingerprint); Assert.Equal(95, saved.Total.Net); Assert.Equal(24.23m, saved.Total.Vat);
            Assert.Equal(1.5m, saved.Lines[0].Quantity); Assert.Null(saved.PeriodFrom); Assert.Equal(Guid.Empty, saved.BookingId);
        }
        using var foreignSession = Session(partition + "-foreign");
        Assert.Null(await new InvoiceRepository(foreignSession).GetByManualSubmissionIdAsync(input.SubmissionId));
        var foreign = Service(foreignSession, partition + "-foreign"); var fp = await foreign.PreviewAsync(input, default);
        var fi = await foreign.IssueAsync(input with { PreviewToken = fp.PreviewToken }, default);
        Assert.NotEqual(results[0].Id, fi.Id); Assert.Equal("2026-0001", fi.InvoiceNumber);
        var next = input with { SubmissionId = Guid.NewGuid() }; var np = await first.PreviewAsync(next, default);
        Assert.Equal("2026-0002", (await first.IssueAsync(next with { PreviewToken = np.PreviewToken }, default)).InvoiceNumber);
        await Assert.ThrowsAsync<ShiftConflictException>(() => second.IssueAsync(input with { Lines = [new("Changed", 1, 1)], PreviewToken = other.PreviewToken }, default));

        // Manual and scheduled company charges share the exact same numbering/reference sequence.
        var branch = Branch.Create(company.Id, "Branch");
        var shift = Shift.Create(company.Id, branch.Id, "Monthly service", new() { Type = ShiftScheduleType.Weekly, WeeklyDay = DayOfWeek.Friday, WeeklyStart = TimeSpan.FromHours(16), WeeklyEnd = TimeSpan.FromHours(18) }, null, new(2026, 10, 1), null);
        shift.CreatedUtc = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        using (var writer = store.LightweightSession(partition)) {
            writer.Store(new ShiftBillingProfile { Id = shift.Id, ShiftId = shift.Id, Plans = [new("Monthly", 150, 25.5m, new(2026, 10, 1))] });
            await writer.SaveChangesAsync();
        }
        var shifts = new Mock<IShiftRepository>(); shifts.Setup(r => r.ListByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([shift]);
        shifts.Setup(r => r.ListOccurrenceExceptionsAsync(shift.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        shifts.Setup(r => r.ListAssignmentHistoryAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        shifts.Setup(r => r.ListOccurrenceAssignmentsAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var branchRepo = new Mock<IBranchRepository>(); branchRepo.Setup(r => r.GetAsync(branch.Id, It.IsAny<CancellationToken>())).ReturnsAsync(branch);
        var companyRepo = new Mock<ICompanyRepository>(); companyRepo.Setup(r => r.GetAsync(company.Id, It.IsAny<CancellationToken>())).ReturnsAsync(company);
        var forms = new Mock<IQualityCycleRepository>(); forms.Setup(r => r.ListFormsAsync(shift.Id, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>())).ReturnsAsync([new QualityCycleForm { ShiftId = shift.Id, EmployeeId = Guid.NewGuid(), IsSubmitted = true, ShiftOccurrenceUtc = new DateTime(2026, 10, 2, 13, 0, 0, DateTimeKind.Utc) }]);
        var profilesRepo = new Mock<ITenantProfileRepository>(); profilesRepo.Setup(r => r.GetAsync(partition, It.IsAny<CancellationToken>())).ReturnsAsync(profile);
        var tenantContext = new Mock<ITenantContext>(); tenantContext.SetupGet(t => t.TenantId).Returns(partition);
        var clockProvider = new Mock<TimeProvider>(); clockProvider.Setup(t => t.GetUtcNow()).Returns(new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero));
        using var shiftSession = Session(partition);
        var shiftService = new ShiftInvoiceService(shifts.Object, new ShiftBillingRepository(shiftSession), new InvoiceRepository(shiftSession),
            companyRepo.Object, branchRepo.Object, forms.Object, profilesRepo.Object, tenantContext.Object, new ShiftWorkReservation(config, shiftSession), clockProvider.Object);
        var shiftInput = new ShiftInvoiceRequest(company.Id, [shift.Id], new(2026, 10, 1), new(2026, 10, 31), input.BillingAddress);
        var shiftPreview = await shiftService.PreviewAsync(shiftInput, default);
        var shiftInvoice = await shiftService.IssueAsync(shiftInput with { PreviewToken = shiftPreview.PreviewToken }, default);
        Assert.Equal("2026-0003", shiftInvoice.InvoiceNumber);
        Assert.Equal(ShiftInvoiceService.PaymentReference("20260003"), shiftInvoice.PaymentReference);
        Assert.Equal(ShiftInvoiceService.PaymentReference("20260001"), results[0].PaymentReference);
    }
}
