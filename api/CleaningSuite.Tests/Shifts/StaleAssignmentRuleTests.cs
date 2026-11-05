using System;
using System.Collections.Generic;
using System.Linq;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Shifts;
using Xunit;

namespace CleaningSuite.Tests.Shifts;

public class StaleAssignmentRuleTests
{
    private static Shift ShiftWith(bool active, Guid? partnerRequestId = null)
    {
        var shift = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Shift",
            new ShiftSchedule { Type = ShiftScheduleType.DailySameTime, DailyStart = TimeSpan.FromHours(8), DailyEnd = TimeSpan.FromHours(10) },
            null, DateTime.UtcNow.Date, null, null);
        shift.IsActive = active;
        shift.PartnerRequestId = partnerRequestId;
        return shift;
    }

    [Fact]
    public void Active_assignments_on_deactivated_shifts_are_stale_and_live_ones_are_not()
    {
        var off = ShiftWith(active: false); var on = ShiftWith(active: true);
        var stale = ShiftAssignment.Create(off.Id, Guid.NewGuid(), null);
        var live = ShiftAssignment.Create(on.Id, Guid.NewGuid(), null);
        var found = StaleAssignmentRule.Find([stale, live], new Dictionary<Guid, Shift> { [off.Id] = off, [on.Id] = on }, new Dictionary<Guid, string>());
        Assert.Equal(new[] { stale.Id }, found.Select(a => a.Id).ToArray());
    }

    [Fact]
    public void Assignments_for_a_cancelled_partner_request_are_stale_but_offered_ones_are_not()
    {
        var requestCancelled = Guid.NewGuid(); var requestOffered = Guid.NewGuid();
        var cancelled = ShiftWith(active: true, requestCancelled); var offered = ShiftWith(active: true, requestOffered);
        var staleAssignment = ShiftAssignment.Create(cancelled.Id, Guid.NewGuid(), null);
        var liveAssignment = ShiftAssignment.Create(offered.Id, Guid.NewGuid(), null);
        var found = StaleAssignmentRule.Find([staleAssignment, liveAssignment],
            new Dictionary<Guid, Shift> { [cancelled.Id] = cancelled, [offered.Id] = offered },
            new Dictionary<Guid, string> { [requestCancelled] = "Cancelled", [requestOffered] = "Offered" });
        Assert.Equal(new[] { staleAssignment.Id }, found.Select(a => a.Id).ToArray());
    }

    [Fact]
    public void Inactive_assignments_and_unknown_shifts_are_never_reported()
    {
        var shift = ShiftWith(active: false);
        var released = ShiftAssignment.Create(shift.Id, Guid.NewGuid(), null);
        released.Release(DateTime.UtcNow);
        var orphan = ShiftAssignment.Create(Guid.NewGuid(), Guid.NewGuid(), null);
        Assert.Empty(StaleAssignmentRule.Find([released, orphan], new Dictionary<Guid, Shift> { [shift.Id] = shift }, new Dictionary<Guid, string>()));
    }
}
