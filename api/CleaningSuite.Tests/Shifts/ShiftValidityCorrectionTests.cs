using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;

namespace CleaningSuite.Tests.Shifts;

public class ShiftValidityCorrectionTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid BranchId = Guid.NewGuid();

    private static Shift Monthly(DateTime? until = null) => Shift.Create(CompanyId, BranchId, "Weekly clean",
        new ShiftSchedule { Type = ShiftScheduleType.Weekly, WeeklyDays = [DayOfWeek.Monday], WeeklyStart = new(9, 0, 0), WeeklyEnd = new(10, 0, 0) },
        null, new DateTime(2026, 10, 1), until ?? new DateTime(2026, 10, 31));

    private static ShiftOccurrenceAssignment Override(Guid shiftId, DateTime original) =>
        ShiftOccurrenceAssignment.Create(shiftId, original, null, OccurrenceAssignmentMode.Replace, [Guid.NewGuid()], "Roster");

    private static ShiftOccurrenceException Cancelled(Guid shiftId, DateTime original) =>
        ShiftOccurrenceException.Create(shiftId, original, original, original.AddHours(1), ShiftOccurrenceExceptionStatus.CompanyCancelled, "Off", null);

    private static QualityCycleForm FormAt(Guid shiftId, DateTime start) => new() { ShiftId = shiftId, ShiftOccurrenceUtc = start, EmployeeId = Guid.NewGuid() };

    [Fact]
    public void Roster_assignment_beyond_the_end_is_removed_but_one_with_work_evidence_is_kept()
    {
        var shift = Monthly();
        var nov2 = new DateTime(2026, 11, 2, 6, 0, 0, DateTimeKind.Utc);
        var nov9 = new DateTime(2026, 11, 9, 6, 0, 0, DateTimeKind.Utc);
        var oct5 = new DateTime(2026, 10, 5, 6, 0, 0, DateTimeKind.Utc);
        var plan = ShiftValidityCorrection.Plan([shift],
            [Override(shift.Id, nov2), Override(shift.Id, nov9), Override(shift.Id, oct5)], [], [FormAt(shift.Id, nov9)], Utc);
        var removal = Assert.Single(plan, x => x.Action == ShiftValidityFindingAction.Remove);
        Assert.Equal(ShiftValidityCorrection.RemovedRosterOverride, removal.Kind);
        Assert.Equal(nov2, removal.OccurrenceUtc);
        Assert.Single(plan, x => x.Action == ShiftValidityFindingAction.Report && x.Kind == ShiftValidityCorrection.ReportedRosterOverride);
    }

    [Fact]
    public void Cancellation_beyond_the_end_is_removed_and_one_inside_the_window_is_left_alone()
    {
        var shift = Monthly();
        var plan = ShiftValidityCorrection.Plan([shift], [],
            [Cancelled(shift.Id, new DateTime(2026, 11, 16, 6, 0, 0, DateTimeKind.Utc)), Cancelled(shift.Id, new DateTime(2026, 10, 19, 6, 0, 0, DateTimeKind.Utc))], [], Utc);
        var removal = Assert.Single(plan);
        Assert.Equal(ShiftValidityCorrection.RemovedOccurrenceException, removal.Kind);
        Assert.Equal(ShiftValidityFindingAction.Remove, removal.Action);
    }

    [Fact]
    public void Extra_visits_beyond_the_end_are_reported_never_removed()
    {
        var shift = Monthly();
        var extra = ShiftOccurrenceException.Create(shift.Id, null, new DateTime(2026, 11, 23, 6, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 23, 7, 0, 0, DateTimeKind.Utc),
            ShiftOccurrenceExceptionStatus.Extra, "Added by staff", Guid.NewGuid());
        var plan = ShiftValidityCorrection.Plan([shift], [], [extra], [], Utc);
        var only = Assert.Single(plan);
        Assert.Equal(ShiftValidityCorrection.ReportedExtraVisit, only.Kind);
        Assert.Equal(ShiftValidityFindingAction.Report, only.Action);
    }

    [Fact]
    public void Visit_moved_out_of_the_window_or_into_it_is_reported_and_never_removed()
    {
        var shift = Monthly();
        var movedOut = ShiftOccurrenceException.Create(shift.Id, new DateTime(2026, 10, 26, 6, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 6, 6, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 6, 7, 0, 0, DateTimeKind.Utc), ShiftOccurrenceExceptionStatus.Moved, "Moved", null);
        // A visit originally scheduled after the end but moved into the window is a real visit inside the window: keep it.
        var movedIn = ShiftOccurrenceException.Create(shift.Id, new DateTime(2026, 11, 2, 6, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 30, 6, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 30, 7, 0, 0, DateTimeKind.Utc), ShiftOccurrenceExceptionStatus.Moved, "Moved", null);
        var plan = ShiftValidityCorrection.Plan([shift], [], [movedOut, movedIn], [], Utc);
        Assert.DoesNotContain(plan, x => x.Action == ShiftValidityFindingAction.Remove);
        Assert.Equal(2, plan.Count(x => x.Kind == ShiftValidityCorrection.ReportedMovedIntoWindow));
    }

    [Fact]
    public void Visit_with_work_evidence_beyond_the_end_is_kept_even_when_cancelled()
    {
        var shift = Monthly();
        var nov16 = new DateTime(2026, 11, 16, 6, 0, 0, DateTimeKind.Utc);
        var plan = ShiftValidityCorrection.Plan([shift], [], [Cancelled(shift.Id, nov16)], [FormAt(shift.Id, nov16)], Utc);
        Assert.DoesNotContain(plan, x => x.Action == ShiftValidityFindingAction.Remove);
    }

    [Fact]
    public void Shifts_without_an_end_date_are_never_touched()
    {
        var shift = Monthly(until: null);
        shift.ValidUntil = null;
        var plan = ShiftValidityCorrection.Plan([shift], [Override(shift.Id, new DateTime(2027, 1, 4, 6, 0, 0, DateTimeKind.Utc))], [], [], Utc);
        Assert.Empty(plan);
    }
}
