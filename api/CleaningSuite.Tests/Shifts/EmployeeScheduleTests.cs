using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CleaningSuite.Application.Companies;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Shifts;

public class EmployeeScheduleTests
{
    private static readonly TimeZoneInfo Helsinki = TimeZoneInfo.FindSystemTimeZoneById("Europe/Helsinki");
    private static EmployeeScheduleVisit Visit(string name, DateTime startUtc) => new(Guid.NewGuid(), name, startUtc, startUtc.AddHours(2));

    [Fact]
    public void Visits_are_grouped_per_cleaner_and_local_day_in_visit_order()
    {
        var cleaner = Guid.NewGuid();
        var morning = Visit("Morning", new DateTime(2026, 11, 2, 6, 0, 0, DateTimeKind.Utc));
        var afternoon = Visit("Afternoon", new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc));
        var nextDay = Visit("Next day", new DateTime(2026, 11, 3, 6, 0, 0, DateTimeKind.Utc));
        var days = ScheduleVisits_Group([(cleaner, afternoon), (cleaner, nextDay), (cleaner, morning)]);
        Assert.Equal(2, days.Count);
        Assert.Equal(new DateTime(2026, 11, 2), days[0].LocalDay);
        Assert.Equal(new[] { morning, afternoon }, days[0].Visits);
        Assert.Equal(new[] { nextDay }, days[1].Visits);
    }

    [Fact]
    public void A_visit_late_in_the_UTC_day_belongs_to_the_next_local_day()
    {
        var cleaner = Guid.NewGuid();
        // 22:30 UTC on 2 Nov is 00:30 on 3 Nov in Helsinki (UTC+2 in November).
        var lateNight = Visit("Late", new DateTime(2026, 11, 2, 22, 30, 0, DateTimeKind.Utc));
        var days = ScheduleVisits_Group([(cleaner, lateNight)]);
        Assert.Equal(new DateTime(2026, 11, 3), Assert.Single(days).LocalDay);
    }

    [Fact]
    public void Cleaners_with_no_visits_do_not_appear()
    {
        Assert.Empty(ScheduleVisits_Group([]));
    }

    // ---- Checklist counts and address ----

    [Fact]
    public void Checklist_counts_ticked_items_against_all_items()
    {
        var form = new QualityCycleForm { Items = [new() { IsChecked = true }, new() { IsChecked = true }, new(), new()] };
        Assert.Equal((2, 4), ScheduleVisits.Checklist(form));
    }

    [Fact]
    public void A_visit_without_a_checklist_has_no_counts()
    {
        Assert.Equal((null, null), ScheduleVisits.Checklist(null));
        Assert.Equal((null, null), ScheduleVisits.Checklist(new QualityCycleForm()));
    }

    [Fact]
    public void The_address_joins_street_postal_code_and_city_and_skips_empty_parts()
    {
        Assert.Equal("Mill Street 4, 00100 Helsinki", ScheduleVisits.Address(new Branch { Street = "Mill Street 4", PostalCode = "00100", City = "Helsinki" }));
        Assert.Equal("Helsinki", ScheduleVisits.Address(new Branch { City = " Helsinki " }));
        Assert.Null(ScheduleVisits.Address(new Branch()));
        Assert.Null(ScheduleVisits.Address(null));
    }

    // ---- The schedule query ----

    private static readonly DateTime Start = new(2026, 12, 7, 7, 0, 0, DateTimeKind.Utc);

    /// <summary>A fixed "now", so a visit's past or future does not depend on the day the tests run.</summary>
    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
    private static readonly TimeProvider BeforeTheVisit = new FixedClock(new DateTime(2026, 12, 1, 9, 0, 0, DateTimeKind.Utc));

    private static Mock<IShiftRepository> RosterOf(Shift shift, ShiftOccurrenceException visit)
    {
        var shifts = new Mock<IShiftRepository>();
        shifts.Setup(x => x.ListForReportingAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { shift });
        shifts.Setup(x => x.ListOccurrenceExceptionsAsync(shift.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { visit });
        shifts.Setup(x => x.ListAssignmentHistoryAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<ShiftAssignment>());
        shifts.Setup(x => x.ListOccurrenceAssignmentsAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<ShiftOccurrenceAssignment>());
        return shifts;
    }

    private static async Task<EmployeeScheduleVisit> OnlyVisit(Shift shift, ShiftOccurrenceException visit, TimeProvider clock, params QualityCycleForm[] saved)
    {
        var forms = new Mock<IQualityCycleRepository>();
        forms.Setup(x => x.ListFormsAsync(It.IsAny<Guid?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(saved.ToList());
        var days = await new EmployeeScheduleQueryHandler(RosterOf(shift, visit).Object, null!, null!, forms.Object, clock: clock)
            .Handle(new EmployeeScheduleQuery(new DateTime(2026, 12, 7), new DateTime(2026, 12, 7)), default);
        return Assert.Single(Assert.Single(days).Visits);
    }

    private static Shift OnCall(string name) =>
        Shift.Create(Guid.NewGuid(), Guid.NewGuid(), name, new ShiftSchedule { Type = ShiftScheduleType.OnCallFlexible }, null, null, null);

    [Fact]
    public async Task A_visit_carries_its_place_notes_work_state_and_the_employees_own_checklist()
    {
        var employee = Guid.NewGuid(); var colleague = Guid.NewGuid();
        var company = new Company { Name = "Quay Dental" };
        var branch = new Branch { Name = "Harbour Road", Street = "Harbour Road 14", PostalCode = "00180", City = "Helsinki", CompanyId = company.Id };
        var shift = Shift.Create(company.Id, branch.Id, "Window clean", new ShiftSchedule { Type = ShiftScheduleType.OnCallFlexible }, null, null, null);
        var visit = ShiftOccurrenceException.Create(shift.Id, null, Start, Start.AddHours(1), ShiftOccurrenceExceptionStatus.Extra, "Added", employee);
        visit.Notes = "Ladder in the lobby cupboard";

        var shifts = new Mock<IShiftRepository>();
        shifts.Setup(x => x.ListForReportingAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { shift });
        shifts.Setup(x => x.ListOccurrenceExceptionsAsync(shift.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { visit });
        shifts.Setup(x => x.ListAssignmentHistoryAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<ShiftAssignment>());
        shifts.Setup(x => x.ListOccurrenceAssignmentsAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<ShiftOccurrenceAssignment>());
        var forms = new Mock<IQualityCycleRepository>();
        forms.Setup(x => x.ListFormsAsync(It.IsAny<Guid?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QualityCycleForm>
            {
                new() { ShiftId = shift.Id, EmployeeId = employee, ShiftOccurrenceUtc = Start, StartedAtUtc = Start,
                    Items = [new() { IsChecked = true }, new() { IsChecked = true }, new(), new()] },
                // Another employee's checklist on the same visit does not count for this employee.
                new() { ShiftId = shift.Id, EmployeeId = colleague, ShiftOccurrenceUtc = Start, Items = [new() { IsChecked = true }] },
            });
        var companies = new Mock<ICompanyRepository>();
        companies.Setup(x => x.GetAsync(company.Id, It.IsAny<CancellationToken>())).ReturnsAsync(company);
        var branches = new Mock<IBranchRepository>();
        branches.Setup(x => x.GetAsync(branch.Id, It.IsAny<CancellationToken>())).ReturnsAsync(branch);

        var days = await new EmployeeScheduleQueryHandler(shifts.Object, null!, null!, forms.Object, companies.Object, branches.Object, BeforeTheVisit)
            .Handle(new EmployeeScheduleQuery(new DateTime(2026, 12, 7), new DateTime(2026, 12, 7)), default);

        var day = Assert.Single(days);
        Assert.Equal(employee, day.EmployeeId);
        var item = Assert.Single(day.Visits);
        Assert.Equal(visit.Id, item.ExceptionId);
        Assert.Equal("Window clean", item.ShiftName);
        Assert.Equal("Quay Dental", item.CompanyName);
        Assert.Equal("Harbour Road", item.BranchName);
        Assert.Equal("Harbour Road 14, 00180 Helsinki", item.Address);
        Assert.Equal("Ladder in the lobby cupboard", item.VisitNotes);
        Assert.Equal(2, item.ChecklistDone);
        Assert.Equal(4, item.ChecklistTotal);
        Assert.False(string.IsNullOrEmpty(item.WorkStatus));
        // Identity and versions for moving or reassigning it with the visit endpoints.
        Assert.Equal(visit.Id, item.ExtraExceptionId);
        Assert.Equal(ShiftOccurrenceExceptionStatus.Extra, item.Status);
        Assert.Null(item.OriginalStartUtc);
        Assert.Equal(employee, item.VisitEmployeeId);
        Assert.Contains(employee, item.AssigneeIds);
        Assert.False(item.IsPartnerBooking);
        // Its checklist has started, so the calendar must not offer to move it.
        Assert.Equal("Started or completed work cannot be moved or reassigned.", item.MoveLockReason);
    }

    [Fact]
    public async Task A_future_visit_without_started_work_can_be_moved()
    {
        var employee = Guid.NewGuid(); var shift = OnCall("Deep clean");
        var visit = ShiftOccurrenceException.Create(shift.Id, null, Start, Start.AddHours(2), ShiftOccurrenceExceptionStatus.Extra, "Added", employee);
        var item = await OnlyVisit(shift, visit, BeforeTheVisit);
        Assert.Null(item.MoveLockReason);
    }

    [Fact]
    public async Task A_past_visit_cannot_be_moved()
    {
        var employee = Guid.NewGuid(); var shift = OnCall("Deep clean");
        var visit = ShiftOccurrenceException.Create(shift.Id, null, Start, Start.AddHours(2), ShiftOccurrenceExceptionStatus.Extra, "Added", employee);
        var item = await OnlyVisit(shift, visit, new FixedClock(new DateTime(2026, 12, 9, 9, 0, 0, DateTimeKind.Utc)));
        Assert.Equal("Past visits cannot be moved or reassigned.", item.MoveLockReason);
    }

    [Fact]
    public async Task A_partner_booking_is_managed_through_partner_requests()
    {
        var employee = Guid.NewGuid(); var shift = OnCall("Partner clean"); shift.PartnerRequestId = Guid.NewGuid();
        var visit = ShiftOccurrenceException.Create(shift.Id, null, Start, Start.AddHours(2), ShiftOccurrenceExceptionStatus.Extra, "Added", employee);
        var item = await OnlyVisit(shift, visit, BeforeTheVisit);
        Assert.True(item.IsPartnerBooking);
        Assert.Equal("Manage this visit through Partner requests.", item.MoveLockReason);
    }

    [Fact]
    public async Task The_schedule_still_works_without_forms_or_company_records()
    {
        var employee = Guid.NewGuid();
        var shift = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Deep clean", new ShiftSchedule { Type = ShiftScheduleType.OnCallFlexible }, null, null, null);
        var visit = ShiftOccurrenceException.Create(shift.Id, null, Start, Start.AddHours(2), ShiftOccurrenceExceptionStatus.Extra, "Added", employee);
        var shifts = new Mock<IShiftRepository>();
        shifts.Setup(x => x.ListForReportingAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { shift });
        shifts.Setup(x => x.ListOccurrenceExceptionsAsync(shift.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { visit });
        shifts.Setup(x => x.ListAssignmentHistoryAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<ShiftAssignment>());
        shifts.Setup(x => x.ListOccurrenceAssignmentsAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<ShiftOccurrenceAssignment>());

        var days = await new EmployeeScheduleQueryHandler(shifts.Object, null!, null!)
            .Handle(new EmployeeScheduleQuery(new DateTime(2026, 12, 7), new DateTime(2026, 12, 7)), default);

        var item = Assert.Single(Assert.Single(days).Visits);
        Assert.Null(item.CompanyName);
        Assert.Null(item.Address);
        Assert.Null(item.ChecklistDone);
        Assert.Null(item.ChecklistTotal);
    }

    private static IReadOnlyList<EmployeeDaySchedule> ScheduleVisits_Group(
        IEnumerable<(Guid EmployeeId, EmployeeScheduleVisit Visit)> visits) =>
        ScheduleVisits.GroupByDay(visits, Helsinki);
}
