using CleaningSuite.Application.Agreements;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.Invoicing;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Agreements;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Invoicing;
using CleaningSuite.Domain.Leads;
using CleaningSuite.Domain.Partners;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Application.Partners;
using CleaningSuite.Infrastructure.Persistence;
using CleaningSuite.Tests.QualityCycle;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Companies;

// The company removal dialog, run against a real PostgreSQL database: what each ticked category removes and keeps.
public class CompanyRemovalDatabaseTests
{
    private sealed record Seeded(string Tenant, Company Company, Branch Branch, Shift Shift, Guid InvoiceId, Guid PaidInvoiceId,
        Guid FormId, Guid AgreementId, Guid PartnerRequestId, Guid PartnerAccountId, Guid TemplateId, Guid LeadId);

    private static async Task<(IDocumentStore Store, Mock<ITenantContext> Context, IServiceProvider Provider)> Start()
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddCleaningMarten(Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!,
            databaseSchemaName: "company_removal_" + Guid.NewGuid().ToString("N"));
        var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        return (store, new Mock<ITenantContext>(), provider);
    }

    // An inactive company with a place, a shift that has a submitted checklist, an issued and a paid invoice, a signed
    // agreement, a partner request, a partner account, a company template and a converted lead.
    private static async Task<Seeded> Seed(IDocumentStore store, Mock<ITenantContext> context)
    {
        var tenant = "removal-" + Guid.NewGuid().ToString("N");
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
        var issued = new Invoice { Id = Guid.NewGuid(), CompanyId = company.Id, BookingNumber = "B-1", InvoiceNumber = "INV-1", Status = Invoice.StatusIssued, IssueDate = day, DueDate = day.AddDays(14) };
        var paid = new Invoice { Id = Guid.NewGuid(), CompanyId = company.Id, BookingNumber = "B-2", InvoiceNumber = "INV-2", Status = Invoice.StatusPaid, IssueDate = day, DueDate = day.AddDays(14) };
        var agreement = new Agreement { Id = Guid.NewGuid(), CompanyId = company.Id, Title = "Service agreement", Code = "SA-1", Status = "Signed" };
        var partnerRequest = new PartnerCleaningRequest { Id = Guid.NewGuid(), CompanyId = company.Id, BranchId = branch.Id };
        var partnerAccount = new Employee { Role = Employee.RolePartner, PartnerCompanyId = company.Id, KeycloakUserId = "partner-" + Guid.NewGuid().ToString("N") };
        var template = QualityCycleTemplate.Create("Checklist", ["Floor"], companyId: company.Id, branchId: branch.Id);
        var lead = new Lead { Id = Guid.NewGuid(), CompanyId = company.Id, Fields = new() { Name = "Converted lead" } };
        using (var writer = store.LightweightSession(tenant))
        {
            writer.Store(company); writer.Store(branch); writer.Store(shift); writer.Store(assignment); writer.Store(form);
            writer.Store(issued); writer.Store(paid); writer.Store(agreement); writer.Store(partnerRequest);
            writer.Store(partnerAccount); writer.Store(template); writer.Store(lead);
            await writer.SaveChangesAsync();
        }
        return new Seeded(tenant, company, branch, shift, issued.Id, paid.Id, form.Id, agreement.Id, partnerRequest.Id,
            partnerAccount.Id, template.Id, lead.Id);
    }

    private static readonly Mock<IEmployeeIdentityProvider> identities = new(MockBehavior.Loose);
    private static readonly Mock<IEmployeeIdentityLock> identityLocks = new(MockBehavior.Loose) { DefaultValue = DefaultValue.Empty };

    private static CompanyRemoval.RemoveHandler Remover(IDocumentStore store, string tenant, out TenantSessionFactory session)
    {
        var context = new Mock<ITenantContext>(); context.SetupGet(x => x.TenantId).Returns(tenant);
        session = new TenantSessionFactory(store, context.Object);
        return new CompanyRemoval.RemoveHandler(new CompanyRepository(session), new BranchRepository(session), new ShiftRepository(session),
            new InvoiceRepository(session), new AgreementRepository(store), context.Object, new PartnerStore(session), identities.Object, identityLocks.Object);
    }

    [QualityCycleDatabaseFact]
    public async Task An_active_company_is_refused_and_nothing_is_removed()
    {
        var (store, context, provider) = await Start(); using var _ = provider as IDisposable;
        var seeded = await Seed(store, context);
        using (var writer = store.LightweightSession(seeded.Tenant))
        {
            var company = await writer.LoadAsync<Company>(seeded.Company.Id);
            company!.IsActive = true; writer.Store(company); await writer.SaveChangesAsync();
        }
        var handler = Remover(store, seeded.Tenant, out var session); using var disposeSession = session;

        var ex = await Assert.ThrowsAsync<InactiveRecordDeleteConflictException>(() =>
            handler.Handle(new(seeded.Company.Id, true, true, true, true), default));

        Assert.Equal("Deactivate the company before deleting it.", ex.Message);
        using var read = store.QuerySession(seeded.Tenant);
        Assert.NotNull(await read.LoadAsync<Shift>(seeded.Shift.Id));
    }

    [QualityCycleDatabaseFact]
    public async Task Places_without_their_shifts_are_refused()
    {
        var (store, context, provider) = await Start(); using var _ = provider as IDisposable;
        var seeded = await Seed(store, context);
        var handler = Remover(store, seeded.Tenant, out var session); using var disposeSession = session;

        var ex = await Assert.ThrowsAsync<InactiveRecordDeleteConflictException>(() =>
            handler.Handle(new(seeded.Company.Id, Branches: true, Shifts: false, Invoices: false), default));

        Assert.Contains("together with their shifts", ex.Message);
        using var read = store.QuerySession(seeded.Tenant);
        Assert.NotNull(await read.LoadAsync<Branch>(seeded.Branch.Id));
    }

    [QualityCycleDatabaseFact]
    public async Task A_paid_invoice_refuses_the_invoices_category_until_it_is_confirmed()
    {
        var (store, context, provider) = await Start(); using var _ = provider as IDisposable;
        var seeded = await Seed(store, context);
        var handler = Remover(store, seeded.Tenant, out var session); using var disposeSession = session;

        var ex = await Assert.ThrowsAsync<InactiveRecordDeleteConflictException>(() =>
            handler.Handle(new(seeded.Company.Id, true, true, true, ConfirmPaidInvoices: false), default));

        Assert.Contains("paid invoice(s)", ex.Message);
        using var read = store.QuerySession(seeded.Tenant);
        Assert.NotNull(await read.LoadAsync<Invoice>(seeded.PaidInvoiceId));
        Assert.NotNull(await read.LoadAsync<Shift>(seeded.Shift.Id));
    }

    [QualityCycleDatabaseFact]
    public async Task Places_and_shifts_without_invoices_remove_those_and_keep_the_company_and_its_invoices()
    {
        var (store, context, provider) = await Start(); using var _ = provider as IDisposable;
        var seeded = await Seed(store, context);
        var handler = Remover(store, seeded.Tenant, out var session); using var disposeSession = session;

        var result = await handler.Handle(new(seeded.Company.Id, Branches: true, Shifts: true, Invoices: false), default);

        Assert.False(result.CompanyRemoved);
        Assert.Equal(1, result.ShiftsRemoved);
        Assert.Equal(1, result.PlacesRemoved);
        using var read = store.QuerySession(seeded.Tenant);
        Assert.Null(await read.LoadAsync<Shift>(seeded.Shift.Id));
        Assert.Null(await read.LoadAsync<Branch>(seeded.Branch.Id));
        Assert.Null(await read.LoadAsync<PartnerCleaningRequest>(seeded.PartnerRequestId));
        Assert.NotNull(await read.LoadAsync<Company>(seeded.Company.Id));
        Assert.NotNull(await read.LoadAsync<Invoice>(seeded.InvoiceId));
        Assert.NotNull(await read.LoadAsync<Invoice>(seeded.PaidInvoiceId));
        Assert.NotNull(await read.LoadAsync<Agreement>(seeded.AgreementId));
        Assert.NotNull(await read.LoadAsync<QualityCycleForm>(seeded.FormId));
        Assert.NotNull(await read.LoadAsync<Employee>(seeded.PartnerAccountId));
    }

    [QualityCycleDatabaseFact]
    public async Task Ticking_all_three_removes_the_company_and_everything_linked_to_it()
    {
        var (store, context, provider) = await Start(); using var _ = provider as IDisposable;
        var seeded = await Seed(store, context);
        var handler = Remover(store, seeded.Tenant, out var session); using var disposeSession = session;

        var result = await handler.Handle(new(seeded.Company.Id, true, true, true, ConfirmPaidInvoices: true), default);

        Assert.True(result.CompanyRemoved);
        Assert.Equal(2, result.InvoicesRemoved);
        Assert.Equal(1, result.PartnerAccountsDeactivated);
        using var read = store.QuerySession(seeded.Tenant);
        Assert.Null(await read.LoadAsync<Company>(seeded.Company.Id));
        Assert.Null(await read.LoadAsync<Shift>(seeded.Shift.Id));
        Assert.Null(await read.LoadAsync<Branch>(seeded.Branch.Id));
        Assert.Null(await read.LoadAsync<Invoice>(seeded.InvoiceId));
        Assert.Null(await read.LoadAsync<Invoice>(seeded.PaidInvoiceId));
        Assert.Null(await read.LoadAsync<Agreement>(seeded.AgreementId));
        Assert.Null(await read.LoadAsync<QualityCycleForm>(seeded.FormId));
        Assert.Null(await read.LoadAsync<PartnerCleaningRequest>(seeded.PartnerRequestId));
        // The partner account is deactivated, unlinked, and its login is disabled; the template and lead are kept, unlinked.
        var account = await read.LoadAsync<Employee>(seeded.PartnerAccountId);
        Assert.NotNull(account);
        Assert.Null(account!.PartnerCompanyId);
        Assert.False(account.IsActive);
        Assert.False(account.IdentitySyncPending);
        identities.Verify(x => x.SynchronizeAccessAsync(seeded.Tenant, seeded.PartnerAccountId, It.IsAny<string>(), It.IsAny<string>(), false, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Null((await read.LoadAsync<QualityCycleTemplate>(seeded.TemplateId))!.CompanyId);
        Assert.Null((await read.LoadAsync<Lead>(seeded.LeadId))!.CompanyId);
    }

    [QualityCycleDatabaseFact]
    public async Task The_summary_counts_what_the_dialog_will_show()
    {
        var (store, context, provider) = await Start(); using var _ = provider as IDisposable;
        var seeded = await Seed(store, context);
        var context2 = new Mock<ITenantContext>(); context2.SetupGet(x => x.TenantId).Returns(seeded.Tenant);
        using var session = new TenantSessionFactory(store, context2.Object);
        var summary = new CompanyRemoval.SummaryHandler(new CompanyRepository(session), new BranchRepository(session), new ShiftRepository(session),
            new InvoiceRepository(session), new AgreementRepository(store), new QualityCycleRepository(session, store), context2.Object, new PartnerStore(session));

        var dto = await summary.Handle(new(seeded.Company.Id), default);

        Assert.Equal(new CompanyRemovalSummaryDto(Branches: 1, Shifts: 1, ShiftsWithChecklists: 1, Invoices: 2, PaidInvoices: 1,
            Agreements: 1, ChecklistSubmissions: 1, PartnerRequests: 1, PartnerAccounts: 1, Templates: 1, Leads: 1), dto);
    }

    [QualityCycleDatabaseFact]
    public async Task The_summary_of_a_missing_company_is_not_found_rather_than_empty()
    {
        var (store, context, provider) = await Start(); using var _ = provider as IDisposable;
        var seeded = await Seed(store, context);
        var context2 = new Mock<ITenantContext>(); context2.SetupGet(x => x.TenantId).Returns(seeded.Tenant);
        using var session = new TenantSessionFactory(store, context2.Object);
        var summary = new CompanyRemoval.SummaryHandler(new CompanyRepository(session), new BranchRepository(session), new ShiftRepository(session),
            new InvoiceRepository(session), new AgreementRepository(store), new QualityCycleRepository(session, store), context2.Object, new PartnerStore(session));

        await Assert.ThrowsAsync<NotFoundException>(() => summary.Handle(new(Guid.NewGuid()), default));
    }

    [QualityCycleDatabaseFact]
    public async Task A_failure_in_the_single_commit_rolls_back_every_staged_step()
    {
        var (store, context, provider) = await Start(); using var _ = provider as IDisposable;
        var seeded = await Seed(store, context);
        // The company delete is the last write in the one commit. Make the database refuse it, so the whole commit must fail.
        var schema = store.Options.DatabaseSchemaName;
        await using (var connection = new Npgsql.NpgsqlConnection(Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $@"
                CREATE OR REPLACE FUNCTION {schema}.refuse_company_delete() RETURNS trigger AS $$
                BEGIN RAISE EXCEPTION 'simulated commit failure'; END; $$ LANGUAGE plpgsql;
                CREATE TRIGGER refuse_company_delete BEFORE DELETE ON {schema}.mt_doc_company
                FOR EACH ROW EXECUTE FUNCTION {schema}.refuse_company_delete();";
            await command.ExecuteNonQueryAsync();
        }
        var handler = Remover(store, seeded.Tenant, out var session); using var disposeSession = session;

        var error = await Assert.ThrowsAnyAsync<Exception>(() =>
            handler.Handle(new(seeded.Company.Id, true, true, true, ConfirmPaidInvoices: true), default));

        Assert.Contains("simulated commit failure", error.ToString());
        using var read = store.QuerySession(seeded.Tenant);
        Assert.NotNull(await read.LoadAsync<Company>(seeded.Company.Id));
        Assert.True((await read.LoadAsync<Shift>(seeded.Shift.Id))!.IsActive);
        Assert.NotNull(await read.LoadAsync<Branch>(seeded.Branch.Id));
        Assert.NotNull(await read.LoadAsync<Invoice>(seeded.InvoiceId));
        Assert.NotNull(await read.LoadAsync<Invoice>(seeded.PaidInvoiceId));
        Assert.NotNull(await read.LoadAsync<Agreement>(seeded.AgreementId));
        Assert.NotNull(await read.LoadAsync<QualityCycleForm>(seeded.FormId));
        Assert.NotNull(await read.LoadAsync<PartnerCleaningRequest>(seeded.PartnerRequestId));
        var account = await read.LoadAsync<Employee>(seeded.PartnerAccountId);
        Assert.True(account!.IsActive);
        Assert.Equal(seeded.Company.Id, account.PartnerCompanyId);
    }
}
