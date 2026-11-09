using CleaningSuite.Application.Common;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Invoicing;
using CleaningSuite.Domain.Agreements;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Invoicing;
using CleaningSuite.Domain.Partners;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Infrastructure.Persistence;
using CleaningSuite.Tests.QualityCycle;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Companies;

// Runs the one-step company clear against a real PostgreSQL database, so the deletes, voids and partner
// removals are checked in stored state, not only in mocked calls.
[Collection("Database integration")]
public class ClearCompanyRelatedDatabaseTests
{
    private sealed record Seeded(string Tenant, Company Company, Branch Branch, Shift Shift, QualityCycleForm Form, Guid InvoiceId, Guid PartnerRequestId, Guid NotificationId, Guid AgreementId);

    private static async Task<(IDocumentStore Store, Mock<ITenantContext> Context, IServiceProvider Provider)> Start()
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddTestMarten(Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!,
            databaseSchemaName: "clear_company_" + Guid.NewGuid().ToString("N"));
        var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var context = new Mock<ITenantContext>();
        return (store, context, provider);
    }

    private static async Task<Seeded> Seed(IDocumentStore store, Mock<ITenantContext> context, string invoiceStatus, bool withAgreement = true)
    {
        var tenant = "clear-" + Guid.NewGuid().ToString("N");
        context.SetupGet(x => x.TenantId).Returns(tenant);
        var day = new DateTime(2026, 10, 16, 8, 0, 0, DateTimeKind.Utc);
        var company = new Company { Id = Guid.NewGuid(), Name = "QA Test Company", IsActive = false };
        var branch = new Branch { Id = Guid.NewGuid(), CompanyId = company.Id, Name = "Main place", IsActive = true };
        var shift = new Shift { Id = Guid.NewGuid(), CompanyId = company.Id, BranchId = branch.Id, Name = "Corridor", IsActive = true,
            Schedule = new() { Type = ShiftScheduleType.DailySameTime, DailyStart = TimeSpan.FromHours(8), DailyEnd = TimeSpan.FromHours(10) } };
        var employee = Guid.NewGuid();
        var assignment = new ShiftAssignment { ShiftId = shift.Id, EmployeeId = employee, IsActive = true, AssignedAtUtc = day.AddDays(-2) };
        var form = QualityCycleForm.Create(shift.Id, shift.Name, employee, "Cleaner", Guid.NewGuid(), "Activities", day, ["Corridor"], day.AddHours(2));
        form.CompanyId = company.Id; form.BranchId = branch.Id;
        form.Start(day); form.Submit([new() { ItemText = "Corridor", IsChecked = true }], null, "Completed", [], day.AddHours(2));
        var invoice = new Invoice { Id = Guid.NewGuid(), CompanyId = company.Id, BookingNumber = "B-1", InvoiceNumber = "INV-1", Status = invoiceStatus, IssueDate = day, DueDate = day.AddDays(14) };
        var partnerRequest = new PartnerCleaningRequest { Id = Guid.NewGuid(), CompanyId = company.Id, BranchId = branch.Id };
        var notification = new PartnerNotification { Id = Guid.NewGuid(), CompanyId = company.Id, BranchId = branch.Id };
        var agreement = new Agreement { Id = Guid.NewGuid(), CompanyId = company.Id, Title = "Service agreement", Code = "SA-1", Status = "Signed" };
        using (var writer = store.LightweightSession(tenant))
        {
            writer.Store(company); writer.Store(branch); writer.Store(shift); writer.Store(assignment); writer.Store(form);
            writer.Store(invoice); writer.Store(partnerRequest); writer.Store(notification);
            if (withAgreement) writer.Store(agreement);
            await writer.SaveChangesAsync();
        }
        return new Seeded(tenant, company, branch, shift, form, invoice.Id, partnerRequest.Id, notification.Id, agreement.Id);
    }

    private static CompanyHandlers.ClearCompanyRelatedCommandHandler Handler(IDocumentStore store, string tenant, out TenantSessionFactory session)
    {
        var context = new Mock<ITenantContext>(); context.SetupGet(x => x.TenantId).Returns(tenant);
        session = new TenantSessionFactory(store, context.Object);
        return new CompanyHandlers.ClearCompanyRelatedCommandHandler(new CompanyRepository(session), new BranchRepository(session),
            new ShiftRepository(session), new AgreementRepository(store), context.Object,
            new PartnerStore(session), null, new InvoiceRepository(session));
    }

    [QualityCycleDatabaseFact]
    public async Task One_step_clear_removes_places_shifts_partner_records_and_voids_invoices_but_keeps_checklists_and_agreements()
    {
        var (store, context, provider) = await Start();
        using var _ = provider as IDisposable;
        var seeded = await Seed(store, context, Invoice.StatusIssued);

        var handler = Handler(store, seeded.Tenant, out var session); using var disposeSession = session;
        var result = await handler.Handle(new(seeded.Company.Id), default);

        Assert.Equal(new ClearCompanyRelatedResultDto(ShiftsDeleted: 1, EmployeesUnassigned: 1, BranchesDeleted: 1,
            InvoicesVoided: 1, PartnerRecordsDeleted: 2, AgreementsDeleted: 0), result);
        using var read = store.QuerySession(seeded.Tenant);
        Assert.Null(await read.LoadAsync<Shift>(seeded.Shift.Id));
        Assert.Null(await read.LoadAsync<Branch>(seeded.Branch.Id));
        Assert.Null(await read.LoadAsync<PartnerCleaningRequest>(seeded.PartnerRequestId));
        Assert.Null(await read.LoadAsync<PartnerNotification>(seeded.NotificationId));
        Assert.Equal(Invoice.StatusVoided, (await read.LoadAsync<Invoice>(seeded.InvoiceId))!.Status);
        Assert.NotNull(await read.LoadAsync<QualityCycleForm>(seeded.Form.Id));
        Assert.NotNull(await read.LoadAsync<Agreement>(seeded.AgreementId));
    }

    [QualityCycleDatabaseFact]
    public async Task A_paid_invoice_refuses_the_clear_and_nothing_is_removed()
    {
        var (store, context, provider) = await Start();
        using var _ = provider as IDisposable;
        var seeded = await Seed(store, context, Invoice.StatusPaid);

        var handler = Handler(store, seeded.Tenant, out var session); using var disposeSession = session;
        var ex = await Assert.ThrowsAsync<InactiveRecordDeleteConflictException>(() => handler.Handle(new(seeded.Company.Id), default));

        Assert.Contains("paid invoices", ex.Message);
        using var read = store.QuerySession(seeded.Tenant);
        Assert.NotNull(await read.LoadAsync<Shift>(seeded.Shift.Id));
        Assert.NotNull(await read.LoadAsync<Branch>(seeded.Branch.Id));
        Assert.NotNull(await read.LoadAsync<PartnerCleaningRequest>(seeded.PartnerRequestId));
        Assert.Equal(Invoice.StatusPaid, (await read.LoadAsync<Invoice>(seeded.InvoiceId))!.Status);
    }

    [QualityCycleDatabaseFact]
    public async Task Signed_agreements_are_deleted_only_when_the_admin_asks()
    {
        var (store, context, provider) = await Start();
        using var _ = provider as IDisposable;
        var seeded = await Seed(store, context, Invoice.StatusIssued);

        var handler = Handler(store, seeded.Tenant, out var session); using var disposeSession = session;
        var result = await handler.Handle(new(seeded.Company.Id, DeleteSignedAgreements: true), default);

        Assert.Equal(1, result.AgreementsDeleted);
        using var read = store.QuerySession(seeded.Tenant);
        Assert.Null(await read.LoadAsync<Agreement>(seeded.AgreementId));
    }
}
