using CleaningSuite.Tests.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.QualityCycle;
using Moq;

namespace CleaningSuite.Tests.Shifts;

public class VisitAssignmentTests
{
    private static readonly DateTime Start = new(2026, 10, 18, 8, 0, 0, DateTimeKind.Utc);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(Start.AddDays(-1)); }
    private sealed class Setup
    {
        public readonly Mock<IShiftRepository> Shifts = new();
        public readonly Mock<IEmployeeRepository> Employees = new();
        public readonly Mock<IQualityCycleRepository> Forms = new();
        public readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid();
        public readonly Shift Shift = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Daily",
            new ShiftSchedule { Type = ShiftScheduleType.DailySameTime, DailyStart = TimeSpan.FromHours(8), DailyEnd = TimeSpan.FromHours(10) },
            null, Start.AddDays(-17).Date, Start.AddDays(4).Date, Guid.NewGuid());
        public readonly List<ShiftAssignment> History = [];
        public readonly List<ShiftOccurrenceAssignment> Overrides = [];
        public readonly List<ShiftOccurrenceException> Exceptions = [];
        public readonly List<QualityCycleForm> Evidence = [];
        public Setup()
        {
            History.Add(new ShiftAssignment { ShiftId = Shift.Id, EmployeeId = A, IsActive = true, AssignedAtUtc = Start.AddDays(-1) });
            Shifts.Setup(x => x.GetAsync(Shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Shift);
            Shifts.Setup(x => x.ListAssignmentHistoryAsync(Shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(History);
            Shifts.Setup(x => x.ListOccurrenceAssignmentsAsync(Shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Overrides);
            Shifts.Setup(x => x.ListOccurrenceExceptionsAsync(Shift.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(Exceptions);
            Shifts.Setup(x => x.ListAsync(null, null, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { Shift });
            Employees.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Guid id, CancellationToken _) => new Employee { Id = id, IsActive = true });
            Shifts.Setup(x => x.SaveAssignmentPeriodsAsync(It.IsAny<IReadOnlyList<ShiftAssignment>>(), It.IsAny<CancellationToken>()))
                .Callback((IReadOnlyList<ShiftAssignment> items, CancellationToken _) => { History.Clear(); History.AddRange(items); })
                .Returns(Task.CompletedTask);
            Forms.Setup(x => x.ListFormsAsync(Shift.Id, It.IsAny<DateTime?>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(Evidence);
        }
        public SetVisitAssigneesCommand Command(OccurrenceAssignmentMode mode = OccurrenceAssignmentMode.Replace, bool handover = false, bool preview = false)
            => new(Shift.Id, Start, null, mode, [B], "Cover", ShiftRosterReader.Version(History), Overrides.FirstOrDefault(x => x.OriginalStartUtc == Start)?.Version, handover, preview);
        public Task<VisitAssignmentResult> Run(SetVisitAssigneesCommand command) => new SetVisitAssigneesHandler(Shifts.Object, Employees.Object, Forms.Object, clock: new Clock()).Handle(command, default);
    }

    [Theory]
    [InlineData(OccurrenceAssignmentMode.Replace, 1)]
    [InlineData(OccurrenceAssignmentMode.Add, 2)]
    [InlineData(OccurrenceAssignmentMode.Inherit, 1)]
    public async Task Single_visit_modes_resolve_team_and_do_not_change_regular_roster(OccurrenceAssignmentMode mode, int count)
    {
        var s = new Setup(); var result = await s.Run(s.Command(mode));
        Assert.Equal(count, result.EmployeeIds.Count);
        if (mode == OccurrenceAssignmentMode.Inherit) Assert.Equal(s.A, Assert.Single(result.EmployeeIds));
        else Assert.Contains(s.B, result.EmployeeIds);
        s.Shifts.Verify(x => x.SaveOccurrenceAssignmentAsync(It.IsAny<ShiftOccurrenceAssignment>(), It.IsAny<CancellationToken>()), Times.Once);
        s.Shifts.Verify(x => x.SaveAssignmentPeriodsAsync(It.IsAny<IReadOnlyList<ShiftAssignment>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Preview_does_not_write_and_duplicate_team_members_are_deduplicated()
    {
        var s = new Setup(); var result = await s.Run(s.Command(preview: true) with { EmployeeIds = [s.B, s.B] });
        Assert.True(result.Preview); Assert.Single(result.EmployeeIds);
        s.Shifts.Verify(x => x.SaveOccurrenceAssignmentAsync(It.IsAny<ShiftOccurrenceAssignment>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Stale_roster_or_override_version_requires_reload()
    {
        var s = new Setup(); await Assert.ThrowsAsync<ShiftConflictException>(() => s.Run(s.Command() with { RosterVersion = "old" }));
        await Assert.ThrowsAsync<ShiftConflictException>(() => s.Run(s.Command() with { AssignmentVersion = Guid.NewGuid() }));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Started_or_submitted_work_is_locked(bool submitted)
    {
        var s = new Setup(); var form = QualityCycleForm.Create(s.Shift.Id, "Daily", s.A, "Cleaner", Guid.NewGuid(), "Tasks", Start, []);
        form.StartedAtUtc = submitted ? null : Start; form.IsSubmitted = submitted; s.Evidence.Add(form);
        await Assert.ThrowsAsync<ShiftConflictException>(() => s.Run(s.Command()));
        s.Shifts.Verify(x => x.SaveOccurrenceAssignmentAsync(It.IsAny<ShiftOccurrenceAssignment>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Cancelled_and_past_visits_and_inactive_employees_cannot_be_assigned()
    {
        var s = new Setup(); s.Exceptions.Add(ShiftOccurrenceException.Create(s.Shift.Id, Start, Start, Start.AddHours(2), ShiftOccurrenceExceptionStatus.CustomerCancelled, "Closed"));
        await Assert.ThrowsAsync<ShiftConflictException>(() => s.Run(s.Command())); s.Exceptions.Clear();
        await Assert.ThrowsAsync<ShiftConflictException>(() => s.Run(s.Command() with { OriginalStartUtc = Start.AddDays(-2) }));
        s.Employees.Setup(x => x.GetByIdAsync(s.B, It.IsAny<CancellationToken>())).ReturnsAsync(new Employee { Id = s.B, IsActive = false });
        await Assert.ThrowsAsync<ShiftConflictException>(() => s.Run(s.Command()));
    }

    [Theory]
    [InlineData(OccurrenceAssignmentMode.Replace)] [InlineData(OccurrenceAssignmentMode.Add)]
    public async Task Handover_preserves_past_and_future_one_off_overrides(OccurrenceAssignmentMode mode)
    {
        var s = new Setup(); var oneOff = ShiftOccurrenceAssignment.Create(s.Shift.Id, Start.AddDays(1), null, OccurrenceAssignmentMode.Replace, [s.A], "One-off"); s.Overrides.Add(oneOff);
        await s.Run(s.Command(mode, handover: true));
        var outgoing = s.History.Single(x => x.EmployeeId == s.A);
        Assert.True(outgoing.CoversVisit(Start.AddDays(-10), Start.AddDays(-10).AddHours(2)));
        Assert.Equal(mode == OccurrenceAssignmentMode.Add, outgoing.CoversVisit(Start, Start.AddHours(2)));
        var incoming = s.History.Single(x => x.EmployeeId == s.B);
        Assert.False(incoming.CoversVisit(Start.AddDays(-1), Start.AddDays(-1).AddHours(2)));
        Assert.True(incoming.CoversVisit(Start, Start.AddHours(2)));
        Assert.Equal(s.A, Assert.Single(oneOff.EmployeeIds));
        s.Shifts.Verify(x => x.SaveAssignmentPeriodsAsync(It.IsAny<IReadOnlyList<ShiftAssignment>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Occurrence_list_exposes_started_work_lock_before_opening_assignment_editor()
    {
        var s = new Setup(); var form = QualityCycleForm.Create(s.Shift.Id, "Daily", s.A, "Cleaner", Guid.NewGuid(), "Tasks", Start, []);
        form.StartedAtUtc = Start; s.Evidence.Add(form);
        var list = await new ShiftHandlers.GetShiftOccurrencesQueryHandler(s.Shifts.Object, forms: s.Forms.Object, clock: new Clock())
            .Handle(new(s.Shift.Id, Start.Date, Start.Date), default);
        var visit = Assert.Single(list); Assert.False(visit.CanAssign); Assert.Contains("Started", visit.AssignmentLockReason);
    }

    [Fact]
    public async Task Daily_own_work_discovers_override_only_employee_and_hides_replaced_employee()
    {
        var s = new Setup();
        s.Overrides.Add(ShiftOccurrenceAssignment.Create(s.Shift.Id, Start, null, OccurrenceAssignmentMode.Replace, [s.B], "Cover"));
        s.Shifts.Setup(x => x.ListAssignmentsByEmployeeAsync(s.A, It.IsAny<CancellationToken>())).ReturnsAsync(s.History);
        s.Shifts.Setup(x => x.ListOccurrenceAssignmentsByEmployeeAsync(s.B, It.IsAny<CancellationToken>())).ReturnsAsync(s.Overrides);
        var bookings = new Mock<CleaningSuite.Application.Bookings.IBookingRepository>();
        bookings.Setup(x => x.ListAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), null, It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<CleaningSuite.Domain.Bookings.Booking>());
        var companies = new Mock<CleaningSuite.Application.Companies.ICompanyRepository>();
        companies.Setup(x => x.GetAsync(s.Shift.CompanyId, It.IsAny<CancellationToken>())).ReturnsAsync(new CleaningSuite.Domain.Companies.Company { Id = s.Shift.CompanyId, IsActive = true });
        var branches = new Mock<CleaningSuite.Application.Companies.IBranchRepository>();
        branches.Setup(x => x.GetAsync(s.Shift.BranchId, It.IsAny<CancellationToken>())).ReturnsAsync(new CleaningSuite.Domain.Companies.Branch { Id = s.Shift.BranchId, CompanyId = s.Shift.CompanyId, IsActive = true });
        var context = new Mock<CleaningSuite.Application.Tenants.ITenantContext>(); context.SetupGet(x => x.TenantId).Returns("test");
        var profiles = new Mock<CleaningSuite.Application.Tenants.ITenantProfileRepository>();
        profiles.Setup(x => x.GetAsync("test", It.IsAny<CancellationToken>())).ReturnsAsync(new CleaningSuite.Domain.Tenants.TenantProfile { TimeZoneId = "UTC" });
        var handler = new CleaningSuite.Application.Access.GetOwnWorkHandler(bookings.Object, s.Shifts.Object, companies.Object, branches.Object, profiles.Object, context.Object);
        Assert.Single((await handler.Handle(new(s.B, "2026-10-18"), default)).Shifts);
        Assert.Empty((await handler.Handle(new(s.A, "2026-10-18"), default)).Shifts);
    }

    [Fact]
    public async Task Old_unstarted_public_checklist_token_does_not_authorize_a_replaced_cleaner()
    {
        var s = new Setup(); var form = QualityCycleForm.Create(s.Shift.Id, "Daily", s.A, "Cleaner", Guid.NewGuid(), "Tasks", Start, [], Start.AddHours(2));
        s.Overrides.Add(ShiftOccurrenceAssignment.Create(s.Shift.Id, Start, null, OccurrenceAssignmentMode.Replace, [s.B], "Cover"));
        s.Forms.Setup(x => x.GetFormByTokenAsync(form.Token, It.IsAny<CancellationToken>())).ReturnsAsync(form);
        var handler = new QualityCycleHandlers.SubmitQualityCycleFormCommandHandler(s.Forms.Object, Mock.Of<CleaningSuite.Application.Common.IUploadContentValidator>(), s.Shifts.Object, s.Employees.Object, WorkAccessFixtures.Reservation, new Clock());
        var error = await Assert.ThrowsAsync<QualityCycleConflictException>(() => handler.Handle(new(form.Token, []), default));
        Assert.Contains("reassigned", error.Message); Assert.False(form.IsSubmitted);
        s.Forms.Verify(x => x.SaveFormAsync(It.IsAny<QualityCycleForm>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Manual_dispatch_does_not_create_tasks_for_a_cancelled_visit()
    {
        var s = new Setup();
        var template = QualityCycleTemplate.Create("Activities", ["Corridor"], null, null, null);
        s.Shift.QualityCycleTemplateId = template.Id;
        s.Forms.Setup(x => x.GetTemplateAsync(template.Id, It.IsAny<CancellationToken>())).ReturnsAsync(template);
        s.Exceptions.Add(ShiftOccurrenceException.Create(s.Shift.Id, Start, Start, Start.AddHours(2), ShiftOccurrenceExceptionStatus.CompanyCancelled, "Closed"));
        var dispatcher = new QualityCycleDispatcher(s.Shifts.Object, s.Forms.Object, s.Employees.Object, new Clock());
        Assert.Equal(0, await dispatcher.DispatchAsync(s.Shift, [new ShiftOccurrence { StartUtc = Start, EndUtc = Start.AddHours(2) }]));
        s.Forms.Verify(x => x.TryCreateFormAsync(It.IsAny<QualityCycleForm>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reports_apply_cross_month_moves_cancellation_extras_and_team_overrides_once()
    {
        var s = new Setup(); var from = Start.Date; var to = from.AddMonths(1);
        s.Shift.CreatedUtc = from.AddDays(-30); s.Shift.ValidUntil = to;
        var original = Start; var destination = from.AddMonths(1).AddHours(12);
        s.Exceptions.Add(ShiftOccurrenceException.Create(s.Shift.Id, original, destination, destination.AddHours(2), ShiftOccurrenceExceptionStatus.Moved, "Move"));
        s.Exceptions.Add(ShiftOccurrenceException.Create(s.Shift.Id, Start.AddDays(1), Start.AddDays(1), Start.AddDays(1).AddHours(2), ShiftOccurrenceExceptionStatus.CustomerCancelled, "Closed"));
        var extra = ShiftOccurrenceException.Create(s.Shift.Id, null, Start.AddHours(3), Start.AddHours(4), ShiftOccurrenceExceptionStatus.Extra, "Extra"); s.Exceptions.Add(extra);
        s.Overrides.Add(ShiftOccurrenceAssignment.Create(s.Shift.Id, Start, null, OccurrenceAssignmentMode.Replace, [s.B], "Cover moved visit"));
        s.Overrides.Add(ShiftOccurrenceAssignment.Create(s.Shift.Id, null, extra.Id, OccurrenceAssignmentMode.Add, [s.B], "Extra team"));
        var zone = TimeZoneInfo.Utc;
        var first = await CleaningSuite.Application.QualityCycle.QualityCycleOccurrences.ResolvedInWindowAsync(s.Shifts.Object, s.Shift, from, to, zone, default);
        Assert.DoesNotContain(first, x => x.Occurrence.StartUtc == original || x.Occurrence.StartUtc == original.AddDays(1));
        Assert.Equal(2, first.Single(x => x.Occurrence.StartUtc == extra.StartUtc).EmployeeIds.Count);
        var next = await CleaningSuite.Application.QualityCycle.QualityCycleOccurrences.ResolvedInWindowAsync(s.Shifts.Object, s.Shift, to, to.AddDays(1), zone, default);
        var moved = next.Single(x => x.Occurrence.StartUtc == destination);
        Assert.Equal(s.B, Assert.Single(moved.EmployeeIds)); Assert.Equal(s.Shift.BranchId, moved.Plan.BranchId);
    }

    [Fact]
    public async Task Overlap_rejects_entire_team_but_adjacent_visits_are_allowed()
    {
        var s = new Setup(); var extra = ShiftOccurrenceException.Create(s.Shift.Id, null, Start, Start.AddHours(2), ShiftOccurrenceExceptionStatus.Extra, "Extra", s.B); s.Exceptions.Add(extra);
        await Assert.ThrowsAsync<ShiftConflictException>(() => s.Run(s.Command()));
        extra.StartUtc = Start.AddHours(2); extra.EndUtc = Start.AddHours(4);
        Assert.Equal(s.B, Assert.Single((await s.Run(s.Command())).EmployeeIds));
    }
}
