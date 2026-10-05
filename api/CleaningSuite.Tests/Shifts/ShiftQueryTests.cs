using CleaningSuite.Application.Common;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Shifts;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Shifts;

public class ShiftQueryTests
{
    [Fact]
    public async Task ListShiftAssignmentsQuery_ReturnsAssignmentsForShift()
    {
        var shiftId = Guid.NewGuid();
        var emp1 = Guid.NewGuid();
        var emp2 = Guid.NewGuid();

        var shiftRepoMock = new Mock<IShiftRepository>();
        var assignments = new List<ShiftAssignment>
        {
            ShiftAssignment.Create(shiftId, emp1, "Morning note"),
            ShiftAssignment.Create(shiftId, emp2, "Afternoon note")
        };

        shiftRepoMock.Setup(r => r.ListAssignmentsByShiftAsync(shiftId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assignments);

        var handler = new ShiftHandlers.ListShiftAssignmentsQueryHandler(shiftRepoMock.Object);
        var result = await handler.Handle(new ListShiftAssignmentsQuery(shiftId), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal(emp1, result[0].EmployeeId);
        Assert.Equal(emp2, result[1].EmployeeId);
    }

    [Fact]
    public async Task ListShiftsQuery_UsesCacheServiceWhenAvailable()
    {
        var companyId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var shiftRepoMock = new Mock<IShiftRepository>();
        var cacheServiceMock = new Mock<ITenantCacheService>();

        var schedule = new ShiftSchedule
        {
            Type = ShiftScheduleType.DailySameTime,
            DailyStart = TimeSpan.FromHours(8),
            DailyEnd = TimeSpan.FromHours(16)
        };
        var shift = Shift.Create(companyId, branchId, "Day Shift", schedule, "Notes", null, null);
        shiftRepoMock.Setup(r => r.ListAsync(companyId, branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Shift> { shift });

        cacheServiceMock.Setup(c => c.GetOrAddAsync(
                "shifts",
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task<IReadOnlyList<ShiftDto>>>>(),
                null,
                It.IsAny<CancellationToken>()))
            .Returns<string, string, Func<CancellationToken, Task<IReadOnlyList<ShiftDto>>>, TimeSpan?, CancellationToken>(
                (prefix, key, factory, exp, ct) => factory(ct)!);

        var handler = new ShiftHandlers.ListShiftsQueryHandler(shiftRepoMock.Object, cacheServiceMock.Object);
        var result = await handler.Handle(new ListShiftsQuery(companyId, branchId), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("Day Shift", result[0].Name);
        cacheServiceMock.Verify(c => c.GetOrAddAsync("shifts", It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task<IReadOnlyList<ShiftDto>>>>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }
}
