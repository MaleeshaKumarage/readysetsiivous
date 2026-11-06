using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Shifts;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Shifts;

public class StaleAssignmentRepairTests
{
    private static Shift ShiftOf(bool active)
    {
        var shift = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Shift",
            new ShiftSchedule { Type = ShiftScheduleType.DailySameTime, DailyStart = TimeSpan.FromHours(8), DailyEnd = TimeSpan.FromHours(10) },
            null, DateTime.UtcNow.Date, null, null);
        shift.IsActive = active;
        return shift;
    }

    [Fact]
    public async Task Releases_assignments_on_deactivated_shifts_and_leaves_live_ones_alone()
    {
        var off = ShiftOf(active: false);
        var on = ShiftOf(active: true);
        var stale = ShiftAssignment.Create(off.Id, Guid.NewGuid(), null);
        var live = ShiftAssignment.Create(on.Id, Guid.NewGuid(), null);

        var repository = new Mock<IShiftRepository>();
        repository.Setup(r => r.ListIncludingInactiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Shift> { off, on });
        repository.Setup(r => r.ListAssignmentsByShiftAsync(off.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShiftAssignment> { stale });
        repository.Setup(r => r.ListAssignmentsByShiftAsync(on.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShiftAssignment> { live });

        var result = await new StaleAssignmentRepairHandler(repository.Object)
            .Handle(new StaleAssignmentRepairCommand(), CancellationToken.None);

        Assert.Equal(new[] { stale.Id }, result.ReleasedAssignmentIds);
        Assert.False(stale.IsActive);
        Assert.True(live.IsActive);
        repository.Verify(r => r.SaveAssignmentAsync(stale, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.SaveAssignmentAsync(live, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Nothing_to_repair_releases_nothing_and_saves_nothing()
    {
        var on = ShiftOf(active: true);
        var live = ShiftAssignment.Create(on.Id, Guid.NewGuid(), null);

        var repository = new Mock<IShiftRepository>();
        repository.Setup(r => r.ListIncludingInactiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Shift> { on });
        repository.Setup(r => r.ListAssignmentsByShiftAsync(on.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShiftAssignment> { live });

        var result = await new StaleAssignmentRepairHandler(repository.Object)
            .Handle(new StaleAssignmentRepairCommand(), CancellationToken.None);

        Assert.Empty(result.ReleasedAssignmentIds);
        repository.Verify(r => r.SaveAssignmentAsync(It.IsAny<ShiftAssignment>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
