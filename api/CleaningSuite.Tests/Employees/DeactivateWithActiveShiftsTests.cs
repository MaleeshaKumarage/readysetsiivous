using CleaningSuite.Application.Common;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.Employees.Commands;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Tests.QualityCycle;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Employees;

public class DeactivateWithActiveShiftsTests
{
    private sealed class Lease(Action release) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() { release(); return ValueTask.CompletedTask; }
    }

    private static (Mock<IEmployeeRepository> employees, Mock<IShiftRepository> shifts, DeactivateEmployeeHandler handler, Employee employee) Setup(
        IReadOnlyList<ShiftAssignment> assignments, IReadOnlyList<Shift> shifts)
    {
        var employee = new Employee { Id = Guid.NewGuid(), Email = "cleaner@example.test", IsActive = true, Role = Employee.RoleEmployee };
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(x => x.GetByIdAsync(employee.Id, It.IsAny<CancellationToken>())).ReturnsAsync(employee);
        var tenant = new Mock<ITenantContext>(); tenant.SetupGet(x => x.TenantId).Returns("synthetic");
        var locks = new Mock<IEmployeeIdentityLock>();
        locks.Setup(x => x.AcquireAsync("synthetic", employee.Email, It.IsAny<CancellationToken>())).ReturnsAsync(new Lease(() => { }));
        var shiftRepository = new Mock<IShiftRepository>();
        shiftRepository.Setup(x => x.ListAssignmentsByEmployeeAsync(employee.Id, It.IsAny<CancellationToken>())).ReturnsAsync(assignments);
        shiftRepository.Setup(x => x.ListByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(shifts);
        var handler = new DeactivateEmployeeHandler(employees.Object, new Mock<IEmployeeIdentityProvider>().Object, tenant.Object, locks.Object,
            WorkAccessFixtures.Reservation, shiftRepository.Object);
        return (employees, shiftRepository, handler, employee);
    }

    [Fact]
    public async Task A_cleaner_still_assigned_to_an_active_shift_is_not_deactivated_and_the_admin_is_told_what_to_do()
    {
        var shift = new Shift { Id = Guid.NewGuid(), IsActive = true };
        var (employees, _, handler, employee) = Setup([ShiftAssignment.Create(shift.Id, Guid.NewGuid(), null)], [shift]);
        employees.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(employee);
        var ex = await Assert.ThrowsAsync<ShiftConflictException>(() => handler.Handle(new(employee.Id), default));
        Assert.Contains("Reassign or remove them first", ex.Message);
        Assert.True(employee.IsActive);
        employees.Verify(x => x.SaveAsync(It.IsAny<Employee>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_cleaner_whose_only_assignments_are_on_deactivated_shifts_can_be_deactivated()
    {
        var shift = new Shift { Id = Guid.NewGuid(), IsActive = false };
        var (employees, _, handler, employee) = Setup([ShiftAssignment.Create(shift.Id, Guid.NewGuid(), null)], [shift]);
        employees.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(employee);
        await handler.Handle(new(employee.Id), default);
        Assert.False(employee.IsActive);
    }
}
