using CleaningSuite.Application.Employees;
using CleaningSuite.Application.Employees.Commands;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Employees;
using Moq;

namespace CleaningSuite.Tests.Employees;

public class EmployeeWorkReservationTests
{
    private sealed class Lease(Action release) : IAsyncDisposable
    { public ValueTask DisposeAsync() { release(); return ValueTask.CompletedTask; } }
    private sealed class Reservation : IShiftWorkReservation
    {
        public bool Held;
        public Task<IAsyncDisposable> AcquireAsync(CancellationToken ct = default)
        { Assert.False(Held); Held = true; return Task.FromResult<IAsyncDisposable>(new Lease(() => Held = false)); }
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public async Task Provider_wait_releases_work_lock_retains_identity_lock_and_preserves_fresh_fields(bool update, bool fail)
    {
        var employee = new Employee { Email = "synthetic@example.invalid", KeycloakUserId = "synthetic-user", IsActive = true };
        var fresh = new Employee { Id = employee.Id, Email = employee.Email, KeycloakUserId = employee.KeycloakUserId,
            IsActive = false, IdentitySyncPending = true, Notes = "Concurrent unrelated change" };
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(x => x.GetByIdAsync(employee.Id, default)).ReturnsAsync(employee);
        employees.Setup(x => x.ReloadAsync(employee.Id, default)).ReturnsAsync(fresh);
        var tenant = new Mock<ITenantContext>(); tenant.SetupGet(x => x.TenantId).Returns("synthetic");
        var identityHeld = false; var locks = new Mock<IEmployeeIdentityLock>();
        locks.Setup(x => x.AcquireAsync("synthetic", employee.Email, default)).ReturnsAsync(() =>
        { identityHeld = true; return new Lease(() => identityHeld = false); });
        var reservation = new Reservation();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var identity = new Mock<IEmployeeIdentityProvider>();
        identity.Setup(x => x.SynchronizeAccessAsync("synthetic", employee.KeycloakUserId, Employee.RoleEmployee, false, default))
            .Returns(async () =>
            {
                Assert.True(identityHeld); Assert.False(reservation.Held);
                Assert.False(employee.IsActive); Assert.True(employee.IdentitySyncPending);
                employees.Verify(x => x.SaveAsync(employee, default), Times.Once);
                entered.SetResult(); await finish.Task;
                if (fail) throw new EmployeeIdentityProviderException("Synthetic provider failure");
            });
        var saved = new List<Employee>();
        employees.Setup(x => x.SaveAsync(It.IsAny<Employee>(), default)).Callback<Employee, CancellationToken>((record, _) => saved.Add(record)).Returns(Task.CompletedTask);
        var operation = update
            ? new UpdateEmployeeHandler(employees.Object, identity.Object, tenant.Object, locks.Object, reservation).Handle(new(employee.Id,
                new EmployeeFields(employee.Email, "Synthetic", "Cleaner", "", Employee.RoleEmployee, null, [], null, null, null, null, null), false), default)
            : new DeactivateEmployeeHandler(employees.Object, identity.Object, tenant.Object, locks.Object, reservation).Handle(new(employee.Id), default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(operation.IsCompleted); Assert.False(reservation.Held); Assert.True(identityHeld);
        // Other tenant work can reserve while the provider is still blocked.
        await using (await reservation.AcquireAsync()) { Assert.True(reservation.Held); }
        finish.SetResult();
        if (fail)
        {
            await Assert.ThrowsAsync<EmployeeIdentityProviderException>(() => operation);
            Assert.True(employee.IdentitySyncPending); Assert.Single(saved);
            employees.Verify(x => x.ReloadAsync(employee.Id, default), Times.Never);
        }
        else
        {
            await operation; Assert.Same(fresh, saved[1]); Assert.Equal("Concurrent unrelated change", saved[1].Notes);
            Assert.False(fresh.IdentitySyncPending);
        }
        Assert.False(identityHeld); Assert.False(reservation.Held);
    }
}
