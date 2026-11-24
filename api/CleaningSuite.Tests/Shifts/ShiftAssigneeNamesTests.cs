using CleaningSuite.Application.Employees;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Shifts;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Shifts;

public class ShiftAssigneeNamesTests
{
    [Fact]
    public async Task Names_are_the_active_assignees_of_each_shift_sorted_and_unknown_employees_are_left_out()
    {
        var shiftA = Guid.NewGuid();
        var shiftB = Guid.NewGuid();
        var ben = new Employee { Id = Guid.NewGuid(), FirstName = "Ben", LastName = "Bell", Email = "ben@example.invalid" };
        var ann = new Employee { Id = Guid.NewGuid(), FirstName = "Ann", LastName = "Adams", Email = "ann@example.invalid" };
        var assignments = new List<ShiftAssignment>
        {
            new() { ShiftId = shiftA, EmployeeId = ben.Id, IsActive = true },
            new() { ShiftId = shiftA, EmployeeId = ann.Id, IsActive = true },
            new() { ShiftId = shiftB, EmployeeId = Guid.NewGuid(), IsActive = true },
        };
        var shifts = new Mock<IShiftRepository>();
        shifts.Setup(r => r.ListActiveAssignmentsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) => assignments.Where(a => ids.Contains(a.ShiftId)).ToList());
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(r => r.ListAsync(true, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Employee> { ben, ann });
        var handler = new ShiftHandlers.ListShiftAssigneeNamesQueryHandler(shifts.Object, employees.Object);

        var names = await handler.Handle(new ListShiftAssigneeNamesQuery(new[] { shiftA, shiftB }), CancellationToken.None);

        Assert.Equal(new[] { "Ann Adams", "Ben Bell" }, names[shiftA]);
        Assert.False(names.ContainsKey(shiftB));
    }
}
