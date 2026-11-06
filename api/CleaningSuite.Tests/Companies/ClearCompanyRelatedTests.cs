using CleaningSuite.Application.Agreements;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Agreements;
using CleaningSuite.Domain.Companies;
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
        }

        public CompanyHandlers.ClearCompanyRelatedCommandHandler Handler() =>
            new(Companies.Object, Branches.Object, Shifts.Object, Forms.Object, Agreements.Object, Tenant);
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
    public async Task Nothing_is_removed_when_the_company_has_checklist_or_visit_history()
    {
        var fixture = new Fixture();
        var company = fixture.Company;
        var shift = new Shift { Id = Guid.NewGuid(), CompanyId = company.Id, IsActive = true };
        fixture.Shifts.Setup(x => x.ListAsync(company.Id, null, It.IsAny<CancellationToken>())).ReturnsAsync([shift]);
        fixture.Forms.Setup(x => x.ListFormsAsync(null, null, null, It.IsAny<CancellationToken>(), null, null, company.Id, null))
            .ReturnsAsync([new QualityCycleForm { ShiftId = shift.Id }]);

        var ex = await Assert.ThrowsAsync<InactiveRecordDeleteConflictException>(() => fixture.Handler().Handle(new(company.Id), default));

        Assert.Contains("must be retained", ex.Message);
        fixture.Shifts.Verify(x => x.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Shifts.Verify(x => x.SaveAsync(It.IsAny<Shift>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Branches.Verify(x => x.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Nothing_is_removed_when_the_company_has_agreement_records()
    {
        var fixture = new Fixture();
        var company = fixture.Company;
        var shift = new Shift { Id = Guid.NewGuid(), CompanyId = company.Id, IsActive = true };
        var branch = Branch.Create(company.Id, "Main place");
        fixture.Shifts.Setup(x => x.ListAsync(company.Id, null, It.IsAny<CancellationToken>())).ReturnsAsync([shift]);
        fixture.Branches.Setup(x => x.ListByCompanyAsync(company.Id, true, It.IsAny<CancellationToken>())).ReturnsAsync([branch]);
        fixture.Agreements.Setup(x => x.ListAsync("readysetsiivous", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Agreement { CompanyId = company.Id }]);

        var ex = await Assert.ThrowsAsync<InactiveRecordDeleteConflictException>(() => fixture.Handler().Handle(new(company.Id), default));

        Assert.Contains("agreement records", ex.Message);
        fixture.Shifts.Verify(x => x.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Branches.Verify(x => x.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_failure_part_way_reports_how_far_it_got_so_the_clear_can_be_run_again()
    {
        var fixture = new Fixture();
        var company = fixture.Company;
        var first = new Shift { Id = Guid.NewGuid(), CompanyId = company.Id, IsActive = false };
        var second = new Shift { Id = Guid.NewGuid(), CompanyId = company.Id, IsActive = false };
        fixture.Shifts.Setup(x => x.ListAsync(company.Id, null, It.IsAny<CancellationToken>())).ReturnsAsync([first, second]);
        fixture.Shifts.Setup(x => x.ListAssignmentsByShiftAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        fixture.Shifts.Setup(x => x.DeleteAsync(second.Id, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));

        var ex = await Assert.ThrowsAsync<InactiveRecordDeleteConflictException>(() => fixture.Handler().Handle(new(company.Id), default));

        Assert.Contains("stopped after 1 of 2 shift(s) were removed", ex.Message);
        Assert.Contains("Run Clear again", ex.Message);
        fixture.Shifts.Verify(x => x.DeleteAsync(first.Id, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Branches.Verify(x => x.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Partner_requested_shifts_are_kept_and_nothing_is_removed()
    {
        var fixture = new Fixture();
        var company = fixture.Company;
        var shift = new Shift { Id = Guid.NewGuid(), CompanyId = company.Id, IsActive = true, PartnerRequestId = Guid.NewGuid() };
        fixture.Shifts.Setup(x => x.ListAsync(company.Id, null, It.IsAny<CancellationToken>())).ReturnsAsync([shift]);

        await Assert.ThrowsAsync<InactiveRecordDeleteConflictException>(() => fixture.Handler().Handle(new(company.Id), default));

        fixture.Shifts.Verify(x => x.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
