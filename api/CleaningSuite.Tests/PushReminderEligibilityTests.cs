using CleaningSuite.Application.Notifications;
using CleaningSuite.Application.Partners;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Notifications;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using Moq;

namespace CleaningSuite.Tests;

public class PushReminderEligibilityTests
{
    [Theory]
    [InlineData("Upcoming", -1, false)]
    [InlineData("Upcoming", 0, true)]
    [InlineData("Upcoming", 29, true)]
    [InlineData("Upcoming", 30, false)]
    [InlineData("Unfinished", -1, false)]
    [InlineData("Unfinished", 0, true)]
    [InlineData("Unfinished", 59, true)]
    [InlineData("Unfinished", 60, false)]
    public async Task Due_and_expiry_boundaries_are_exact(string kind, int minutesAfterDue, bool expected)
    {
        var fixture = Create(kind, minutesAfterDue);
        Assert.Equal(expected, await fixture.Scheduler.IsEligibleAsync(fixture.Intent));
    }

    [Theory]
    [InlineData("Upcoming", true, false, false, false)]
    [InlineData("Upcoming", false, true, false, false)]
    [InlineData("Unfinished", false, false, false, false)]
    [InlineData("Unfinished", true, true, false, false)]
    [InlineData("Unfinished", true, false, true, false)]
    [InlineData("Unfinished", true, false, false, true)]
    public async Task Live_work_evidence_and_roster_suppress_obsolete_alerts(string kind, bool started, bool ended, bool submitted, bool removed)
    {
        var fixture = Create(kind, 1);
        fixture.Evidence.StartedAtUtc = started ? fixture.Intent.OccurrenceStartUtc : null;
        fixture.Evidence.EndedAtUtc = ended ? fixture.Intent.OccurrenceEndUtc : null;
        fixture.Evidence.IsSubmitted = submitted;
        if (removed) fixture.Visit.EmployeeId = Guid.NewGuid();
        Assert.False(await fixture.Scheduler.IsEligibleAsync(fixture.Intent));
    }

    [Fact]
    public async Task A_moved_visit_invalidates_old_pending_schedule()
    {
        var fixture = Create("Upcoming", 1);
        fixture.Visit.StartUtc = fixture.Visit.StartUtc.AddHours(2);
        fixture.Visit.EndUtc = fixture.Visit.EndUtc.AddHours(2);
        Assert.False(await fixture.Scheduler.IsEligibleAsync(fixture.Intent));
    }

    private static Fixture Create(string kind, int minutesAfterDue)
    {
        // An overnight visit crosses Helsinki's autumn DST transition.
        var start = new DateTime(2026, 10, 24, 22, 30, 0, DateTimeKind.Utc);
        var end = start.AddHours(6); var employee = Guid.NewGuid();
        var shift = new Shift { IsActive = true, Schedule = new() { Type = ShiftScheduleType.OnCallFlexible } };
        var visit = ShiftOccurrenceException.Create(shift.Id, null, start, end, ShiftOccurrenceExceptionStatus.Extra, "test", employee);
        var due = kind == "Upcoming" ? start.AddMinutes(-30) : end.AddMinutes(30);
        var intent = new PushIntent { Kind = kind, EmployeeId = employee, ShiftId = shift.Id, ExtraExceptionId = visit.Id,
            OccurrenceStartUtc = start, OccurrenceEndUtc = end, DueUtc = due,
            ExpiresUtc = kind == "Upcoming" ? start : due.AddMinutes(60) };
        var evidence = new QualityCycleForm { StartedAtUtc = kind == "Unfinished" ? start : null };
        var shifts = new Mock<IShiftRepository>();
        shifts.Setup(x => x.GetAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shift);
        shifts.Setup(x => x.ListOccurrenceExceptionsAsync(shift.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync([visit]);
        shifts.Setup(x => x.ListAssignmentHistoryAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        shifts.Setup(x => x.ListOccurrenceAssignmentsAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var forms = new Mock<IQualityCycleRepository>();
        forms.Setup(x => x.GetFormByShiftOccurrenceAsync(shift.Id, employee, start, It.IsAny<CancellationToken>())).ReturnsAsync(evidence);
        var scheduler = new PushReminderScheduler(Mock.Of<IPartnerStore>(), shifts.Object, forms.Object,
            Mock.Of<ITenantProfileRepository>(), Mock.Of<ITenantContext>(), Mock.Of<IPushOutbox>(),
            Mock.Of<IShiftWorkReservation>(), new FixedClock(new DateTimeOffset(due.AddMinutes(minutesAfterDue))));
        return new(scheduler, intent, evidence, visit);
    }
    private sealed record Fixture(PushReminderScheduler Scheduler, PushIntent Intent, QualityCycleForm Evidence, ShiftOccurrenceException Visit);
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}
