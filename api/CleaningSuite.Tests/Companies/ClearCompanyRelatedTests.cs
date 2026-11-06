using CleaningSuite.Application.Agreements;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.Invoicing;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Agreements;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Invoicing;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Companies;

public class ClearCompanyRelatedTests
{
    private sealed class Fixture
    {
        public Company Company { get; } = Company.Create("1234567-8", "Customer");
        public Mock<ICompanyRepository> Companies { get; } = new();
        public Mock<IBranchRepository> Branches { get; } = new(MockBehavior.Loose) { DefaultValue = DefaultValue.Empty };
        public Mock<IShiftRepository> Shifts { get; } = new(MockBehavior.Loose) { DefaultValue = DefaultValue.Empty };
        public Mock<IQualityCycleRepository> Forms { get; } = new(MockBehavior.Loose) { DefaultValue = DefaultValue.Empty };
        public Mock<IAgreementRepository> Agreements { get; } = new(MockBehavior.Loose) { DefaultValue = DefaultValue.Empty };
        public Mock<IInvoiceRepository> Invoices { get; } = new(MockBehavior.Loose) { DefaultValue = DefaultValue.Empty };
        public ITenantContext Tenant { get; }

        public Fixture()
        {
            Companies.Setup(x => x.GetAsync(Company.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Company);
            // IReadOnlyList results are not covered by DefaultValue.Empty, so the lookups the handler makes are stubbed explicitly.
            Forms.Setup(x => x.ListFormsAsync(It.IsAny<Guid?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>()))
                .ReturnsAsync([]);
            Agreements.Setup(x => x.ListAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
            var tenant = new Mock<ITenantContext>();
            tenant.Setup(x => x.TenantId).Returns("readysetsiivous");
            Tenant = tenant.Object;
            Branches.Setup(x => x.ListByCompanyAsync(Company.Id, true, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<Branch>());
            Shifts.Setup(x => x.ListAsync(Company.Id, null, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<Shift>());
            Invoices.Setup(x => x.ListAsync(null, null, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<Invoice>());
            Agreements.Setup(x => x.ListAsync("readysetsiivous", It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<Agreement>());
        }

        public CompanyHandlers.ClearCompanyRelatedCommandHandler Handler() =>
            new(Companies.Object, Branches.Object, Shifts.Object, Agreements.Object, Tenant, invoices: Invoices.Object);
    }

    [Fact]
    public async Task Deletes_the_company_shifts_unassigns_employees_and_clears_places()
    {
        var fixture = new Fixture();
        var company = fixture.Company;
        var shift = new Shift { Id = Guid.NewGuid(), CompanyId = company.Id, IsActive = true };
        var assignment = new ShiftAssignment { Id = Guid.NewGuid(), ShiftId = shift.Id, EmployeeId = Guid.NewGuid(), IsActive = true, AssignedAtUtc = DateTime.UtcNow.AddDays(-1) };
        var branch = Branch.Create(company.Id, "Main place");
        fixture.Shifts.Setup(x => x.ListAsync(company.Id, null, It.IsAny<CancellationToken>())).ReturnsAsync([shift]);
        fixture.Shifts.Setup(x => x.ListAssignmentsByShiftAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync([assignment]);
        fixture.Branches.Setup(x => x.ListByCompanyAsync(company.Id, true, It.IsAny<CancellationToken>())).ReturnsAsync([branch]);

        var result = await fixture.Handler().Handle(new(company.Id), default);

        Assert.Equal(new ClearCompanyRelatedResultDto(ShiftsDeleted: 1, EmployeesUnassigned: 1, BranchesDeleted: 1), result);
        Assert.False(assignment.IsActive);
        fixture.Shifts.Verify(x => x.SaveAssignmentAsync(It.Is<ShiftAssignment>(a => a.Id == assignment.Id), It.IsAny<CancellationToken>()), Times.Once);
        fixture.Shifts.Verify(x => x.DeleteAsync(shift.Id, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Branches.Verify(x => x.SaveAsync(It.Is<Branch>(b => b.Id == branch.Id && !b.IsActive), It.IsAny<CancellationToken>()), Times.Once);
        fixture.Branches.Verify(x => x.DeleteAsync(branch.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Checklist_submissions_do_not_block_the_clear_and_are_kept()
    {
        var fixture = new Fixture();
        var company = fixture.Company;
        var shift = new Shift { Id = Guid.NewGuid(), CompanyId = company.Id, IsActive = true };
        fixture.Shifts.Setup(x => x.ListAsync(company.Id, null, It.IsAny<CancellationToken>())).ReturnsAsync([shift]);
        fixture.Shifts.Setup(x => x.ListAssignmentsByShiftAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        fixture.Forms.Setup(x => x.ListFormsAsync(null, null, null, It.IsAny<CancellationToken>(), null, null, company.Id, null))
            .ReturnsAsync([new QualityCycleForm { ShiftId = shift.Id }]);

        var result = await fixture.Handler().Handle(new(company.Id), default);

        Assert.Equal(1, result.ShiftsDeleted);
        fixture.Shifts.Verify(x => x.DeleteAsync(shift.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Signed_agreements_are_kept_unless_the_admin_asks_for_them_to_be_deleted()
    {
        var fixture = new Fixture();
        var company = fixture.Company;
        var agreement = new Agreement { Id = Guid.NewGuid(), CompanyId = company.Id };
        fixture.Agreements.Setup(x => x.ListAsync("readysetsiivous", It.IsAny<CancellationToken>())).ReturnsAsync([agreement]);

        var kept = await fixture.Handler().Handle(new(company.Id), default);
        Assert.Equal(0, kept.AgreementsDeleted);
        fixture.Agreements.Verify(x => x.DeleteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);

        var removed = await fixture.Handler().Handle(new(company.Id, DeleteSignedAgreements: true), default);
        Assert.Equal(1, removed.AgreementsDeleted);
        fixture.Agreements.Verify(x => x.DeleteAsync("readysetsiivous", agreement.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Open_invoices_are_voided_and_a_paid_invoice_refuses_the_whole_clear()
    {
        var fixture = new Fixture();
        var company = fixture.Company;
        var shift = new Shift { Id = Guid.NewGuid(), CompanyId = company.Id, IsActive = true };
        var issued = new Invoice { Id = Guid.NewGuid(), CompanyId = company.Id, Status = Invoice.StatusIssued };
        var draft = new Invoice { Id = Guid.NewGuid(), CompanyId = company.Id, Status = Invoice.StatusDraft };
        fixture.Shifts.Setup(x => x.ListAsync(company.Id, null, It.IsAny<CancellationToken>())).ReturnsAsync([shift]);
        fixture.Shifts.Setup(x => x.ListAssignmentsByShiftAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        fixture.Invoices.Setup(x => x.ListAsync(null, null, It.IsAny<CancellationToken>())).ReturnsAsync([issued, draft]);

        var result = await fixture.Handler().Handle(new(company.Id), default);

        Assert.Equal(2, result.InvoicesVoided);
        Assert.Equal(Invoice.StatusVoided, issued.Status);
        Assert.Equal(Invoice.StatusVoided, draft.Status);
        fixture.Invoices.Verify(x => x.SaveAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task A_paid_invoice_refuses_the_clear_before_anything_is_removed()
    {
        var fixture = new Fixture();
        var company = fixture.Company;
        var shift = new Shift { Id = Guid.NewGuid(), CompanyId = company.Id, IsActive = true };
        fixture.Shifts.Setup(x => x.ListAsync(company.Id, null, It.IsAny<CancellationToken>())).ReturnsAsync([shift]);
        fixture.Invoices.Setup(x => x.ListAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Invoice { CompanyId = company.Id, Status = Invoice.StatusPaid }]);

        var ex = await Assert.ThrowsAsync<InactiveRecordDeleteConflictException>(() => fixture.Handler().Handle(new(company.Id), default));

        Assert.Contains("paid invoices", ex.Message);
        fixture.Shifts.Verify(x => x.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Invoices.Verify(x => x.SaveAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_unexpected_failure_part_way_reports_how_far_it_got_so_the_clear_can_be_run_again()
    {
        var fixture = new Fixture();
        var company = fixture.Company;
        var first = new Shift { Id = Guid.NewGuid(), CompanyId = company.Id, IsActive = false };
        var second = new Shift { Id = Guid.NewGuid(), CompanyId = company.Id, IsActive = false };
        fixture.Shifts.Setup(x => x.ListAsync(company.Id, null, It.IsAny<CancellationToken>())).ReturnsAsync([first, second]);
        fixture.Shifts.Setup(x => x.ListAssignmentsByShiftAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        fixture.Shifts.Setup(x => x.DeleteAsync(second.Id, It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("db timeout"));
        fixture.Branches.Setup(x => x.ListByCompanyAsync(company.Id, true, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Handler().Handle(new(company.Id), default));

        Assert.Contains("stopped after removing 1 of 2 shift(s)", ex.Message);
        Assert.Contains("Run Clear again", ex.Message);
        Assert.IsType<TimeoutException>(ex.InnerException);
        fixture.Shifts.Verify(x => x.DeleteAsync(first.Id, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Branches.Verify(x => x.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_specific_refusal_from_a_delete_passes_through_unchanged()
    {
        var fixture = new Fixture();
        var company = fixture.Company;
        var first = new Shift { Id = Guid.NewGuid(), CompanyId = company.Id, IsActive = false };
        fixture.Shifts.Setup(x => x.ListAsync(company.Id, null, It.IsAny<CancellationToken>())).ReturnsAsync([first]);
        fixture.Shifts.Setup(x => x.ListAssignmentsByShiftAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        fixture.Shifts.Setup(x => x.DeleteAsync(first.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InactiveRecordDeleteConflictException("This shift has started or completed visits that may not be invoiced yet."));

        var ex = await Assert.ThrowsAsync<InactiveRecordDeleteConflictException>(() => fixture.Handler().Handle(new(company.Id), default));

        Assert.Equal("This shift has started or completed visits that may not be invoiced yet.", ex.Message);
    }

    [Fact]
    public async Task Partner_requested_shifts_are_removed_with_the_company()
    {
        var fixture = new Fixture();
        var company = fixture.Company;
        var shift = new Shift { Id = Guid.NewGuid(), CompanyId = company.Id, IsActive = true, PartnerRequestId = Guid.NewGuid() };
        fixture.Shifts.Setup(x => x.ListAsync(company.Id, null, It.IsAny<CancellationToken>())).ReturnsAsync([shift]);
        fixture.Shifts.Setup(x => x.ListAssignmentsByShiftAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var result = await fixture.Handler().Handle(new(company.Id), default);

        Assert.Equal(1, result.ShiftsDeleted);
        fixture.Shifts.Verify(x => x.DeleteAsync(shift.Id, It.IsAny<CancellationToken>()), Times.Once);
    }
}
