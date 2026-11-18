using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Shifts;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Shifts;

public class StaleAssignmentDryRunTests
{
    private static Shift ShiftWith(string name, bool active, Guid? partnerRequestId = null)
    {
        var shift = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), name,
            new ShiftSchedule { Type = ShiftScheduleType.DailySameTime, DailyStart = TimeSpan.FromHours(8), DailyEnd = TimeSpan.FromHours(10) },
            null, DateTime.UtcNow.Date, null, null);
        shift.IsActive = active;
        shift.PartnerRequestId = partnerRequestId;
        return shift;
    }

    [Fact]
    public async Task Reports_stale_assignments_with_reasons_and_skips_live_ones_without_writing()
    {
        var deactivated = ShiftWith("Old shift", active: false);
        var cancelledRequest = Guid.NewGuid();
        var cancelled = ShiftWith("Partner shift", active: true, partnerRequestId: cancelledRequest);
        var live = ShiftWith("Live shift", active: true, partnerRequestId: Guid.NewGuid());
        var staleOnDeactivated = ShiftAssignment.Create(deactivated.Id, Guid.NewGuid(), null);
        var staleOnCancelled = ShiftAssignment.Create(cancelled.Id, Guid.NewGuid(), null);
        var liveAssignment = ShiftAssignment.Create(live.Id, Guid.NewGuid(), null);

        var shifts = new Mock<IShiftRepository>();
        shifts.Setup(x => x.ListIncludingInactiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync([deactivated, cancelled, live]);
        shifts.Setup(x => x.ListAssignmentsByShiftAsync(deactivated.Id, It.IsAny<CancellationToken>())).ReturnsAsync([staleOnDeactivated]);
        shifts.Setup(x => x.ListAssignmentsByShiftAsync(cancelled.Id, It.IsAny<CancellationToken>())).ReturnsAsync([staleOnCancelled]);
        shifts.Setup(x => x.ListAssignmentsByShiftAsync(live.Id, It.IsAny<CancellationToken>())).ReturnsAsync([liveAssignment]);

        var items = await StaleAssignmentDryRun.Run(shifts.Object,
            ids => Task.FromResult<IReadOnlyDictionary<Guid, string>>(ids.ToDictionary(id => id, id => id == cancelledRequest ? "Cancelled" : "Accepted")));

        Assert.Equal(2, items.Count);
        Assert.Contains(items, i => i.AssignmentId == staleOnDeactivated.Id && i.Reason == "Shift is deactivated" && i.ShiftName == "Old shift");
        Assert.Contains(items, i => i.AssignmentId == staleOnCancelled.Id && i.Reason == "Partner request is cancelled" && i.ShiftName == "Partner shift");
        Assert.DoesNotContain(items, i => i.AssignmentId == liveAssignment.Id);

        // A dry run must never write: no save, delete or deactivation may be called.
        shifts.Verify(x => x.SaveAssignmentAsync(It.IsAny<ShiftAssignment>(), It.IsAny<CancellationToken>()), Times.Never);
        shifts.Verify(x => x.SaveAssignmentPeriodsAsync(It.IsAny<IReadOnlyList<ShiftAssignment>>(), It.IsAny<CancellationToken>()), Times.Never);
        shifts.Verify(x => x.SaveDeactivationAsync(It.IsAny<Shift>(), It.IsAny<IReadOnlyList<ShiftAssignment>>(), It.IsAny<CancellationToken>()), Times.Never);
        shifts.Verify(x => x.DeleteAssignmentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Returns_nothing_and_skips_partner_lookup_when_no_shift_is_linked_to_a_request()
    {
        var shift = ShiftWith("Plain shift", active: true);
        var shifts = new Mock<IShiftRepository>();
        shifts.Setup(x => x.ListIncludingInactiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync([shift]);
        shifts.Setup(x => x.ListAssignmentsByShiftAsync(shift.Id, It.IsAny<CancellationToken>())).ReturnsAsync([ShiftAssignment.Create(shift.Id, Guid.NewGuid(), null)]);
        var lookups = 0;

        var items = await StaleAssignmentDryRun.Run(shifts.Object, ids => { lookups++; return Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>()); });

        Assert.Empty(items);
        Assert.Equal(0, lookups);
    }
}
