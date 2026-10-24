using CleaningSuite.Application.Employees;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Shifts;
using Moq;

namespace CleaningSuite.Tests.QualityCycle;

// Explicit active dependencies for tests whose subject is clocks/checklist validation.
// Access-denial and concurrency tests use their own current-state repositories.
internal static class WorkAccessFixtures
{
    public static IEmployeeRepository ActiveEmployees()
    {
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new Employee { Id = id, IsActive = true });
        return employees.Object;
    }
    public static IShiftRepository ActiveShifts()
    {
        var shifts = new Mock<IShiftRepository>();
        shifts.Setup(x => x.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new Shift { Id = id, IsActive = true });
        return shifts.Object;
    }
    public static IShiftWorkReservation Reservation { get; } = new UncontendedReservation();
    private sealed class UncontendedReservation : IShiftWorkReservation
    {
        public Task<IAsyncDisposable> AcquireAsync(CancellationToken ct = default) => Task.FromResult<IAsyncDisposable>(new Lease());
        private sealed class Lease : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    }
}
