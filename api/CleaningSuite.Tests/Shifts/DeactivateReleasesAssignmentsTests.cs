using System;
using System.Collections.Generic;
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
        repo.Verify(r => r.SaveAssignmentAsync(active, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.SaveAssignmentAsync(alreadyEnded, It.IsAny<CancellationToken>()), Times.Never);
    }
}
