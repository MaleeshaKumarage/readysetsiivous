using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Shifts;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Shifts;

public class DeactivateReleasesAssignmentsTests
{
    [Fact]
    public async Task Deactivating_a_shift_releases_its_active_assignments_and_keeps_inactive_ones()
    {
        var shiftId = Guid.NewGuid();
        var shift = new Shift { Id = shiftId, IsActive = true, Name = "QA deactivate" };
        var active = new ShiftAssignment { ShiftId = shiftId, EmployeeId = Guid.NewGuid(), IsActive = true };
        var alreadyEnded = new ShiftAssignment { ShiftId = shiftId, EmployeeId = Guid.NewGuid(), IsActive = false };
        var repo = new Mock<IShiftRepository>();
        repo.Setup(r => r.GetAsync(shiftId, It.IsAny<CancellationToken>())).ReturnsAsync(shift);
        repo.Setup(r => r.ListAssignmentsByShiftAsync(shiftId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShiftAssignment> { active, alreadyEnded });

        await new ShiftHandlers.DeactivateShiftCommandHandler(repo.Object).Handle(new DeactivateShiftCommand(shiftId), default);

        Assert.False(shift.IsActive);
        Assert.False(active.IsActive);
        Assert.NotNull(active.UnassignedAtUtc);
        repo.Verify(r => r.SaveDeactivationAsync(shift,
            It.Is<IReadOnlyList<ShiftAssignment>>(l => l.Count == 1 && ReferenceEquals(l[0], active)),
            It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.SaveAssignmentAsync(It.IsAny<ShiftAssignment>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Releasing_closes_the_open_history_period_so_later_visits_are_not_attributed()
    {
        var now = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
        var assignment = new ShiftAssignment { ShiftId = Guid.NewGuid(), EmployeeId = Guid.NewGuid(), IsActive = true, AssignedAtUtc = now.AddDays(-30) };
        assignment.Periods.Add(new ShiftAssignmentPeriod { FromUtc = now.AddDays(-30) });

        assignment.Release(now);

        Assert.Equal(now, assignment.Periods.Single().ToUtc);
        var laterStart = now.AddDays(3);
        Assert.False(assignment.CoversVisit(laterStart, laterStart.AddHours(2)));
        Assert.False(assignment.Overlaps(laterStart, laterStart.AddHours(2)));
    }
}
