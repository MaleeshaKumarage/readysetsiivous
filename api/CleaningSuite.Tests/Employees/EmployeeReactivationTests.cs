using CleaningSuite.Application.Common;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.Employees.Commands;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Employees;
using Moq;
using System.Text.Json;

namespace CleaningSuite.Tests.Employees;

public class EmployeeReactivationTests
{
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IEmployeeIdentityProvider> _identity = new(MockBehavior.Strict);
    private readonly Mock<IEmployeeIdentityLock> _locks = new();
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Employee _employee = new() {
        Email = "Cleaner@Example.invalid", IsActive = false, Role = Employee.RoleManager,
        FirstName = "Existing", LastName = "Cleaner", Phone = "123", ColorHex = "#abcdef",
        Skills = ["windows"], ServiceAreas = ["north"], PayRate = 42,
        Certifications = [new() { Name = "Safety", ExpiresAtUtc = DateTime.UtcNow.AddYears(1) }],
        Notes = "Preserve", DefaultHours = new() { ["Monday"] = new() },
        InvitedAtUtc = DateTime.UtcNow.AddMonths(-2), InviteLastSentUtc = DateTime.UtcNow.AddMonths(-1),
        InviteLastAttemptUtc = DateTime.UtcNow.AddDays(-1), InviteError = "historical error"
    };
    private sealed class Lease : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    public EmployeeReactivationTests()
    {
        _tenant.SetupGet(x => x.TenantId).Returns("tenant-a");
        _employees.Setup(x => x.GetByIdAsync(_employee.Id, default)).ReturnsAsync(_employee);
        _employees.Setup(x => x.ReloadAsync(_employee.Id, default)).ReturnsAsync(_employee);
        _locks.Setup(x => x.AcquireAsync("tenant-a", _employee.Email, default)).ReturnsAsync(new Lease());
    }
    private ReactivateEmployeeHandler Handler() => new(_employees.Object, _identity.Object, _tenant.Object, _locks.Object);

    [Theory]
    [InlineData("")]
    [InlineData("linked-user")]
    public async Task RestoresExistingProfileAndPreservesAllMetadata(string login)
    {
        _employee.KeycloakUserId = login;
        var before = JsonSerializer.SerializeToNode(_employee)!;
        if (login.Length > 0) _identity.Setup(x => x.SynchronizeAccessAsync("tenant-a", login, Employee.RoleManager, true, default))
            .Callback(() => {
                Assert.True(_employee.IdentitySyncPending);
                _employees.Verify(x => x.SaveAsync(_employee, default), Times.Once);
            }).Returns(Task.CompletedTask);
        await Handler().Handle(new(_employee.Id), default);
        Assert.True(_employee.IsActive);
        Assert.False(_employee.IdentitySyncPending);
        var after = JsonSerializer.SerializeToNode(_employee)!;
        foreach (var key in new[] { "IsActive", "IdentitySyncPending", "UpdatedUtc" }) { before.AsObject().Remove(key); after.AsObject().Remove(key); }
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(before, after));
        _employees.Verify(x => x.ReloadAsync(_employee.Id, default), Times.Once);
        _employees.Verify(x => x.SaveAsync(_employee, default), Times.Exactly(login.Length > 0 ? 2 : 1));
    }

    [Fact]
    public async Task FailedSyncPersistsPendingAndAlreadyActiveRetryHealsIt()
    {
        _employee.KeycloakUserId = "linked-user";
        _identity.SetupSequence(x => x.SynchronizeAccessAsync("tenant-a", "linked-user", Employee.RoleManager, true, default))
            .ThrowsAsync(new EmployeeIdentityProviderException("Unavailable")).Returns(Task.CompletedTask);
        await Assert.ThrowsAsync<EmployeeIdentityProviderException>(() => Handler().Handle(new(_employee.Id), default));
        Assert.True(_employee.IsActive); Assert.True(_employee.IdentitySyncPending);
        _employees.Verify(x => x.SaveAsync(_employee, default), Times.Once);
        await Handler().Handle(new(_employee.Id), default);
        Assert.False(_employee.IdentitySyncPending);
        _identity.Verify(x => x.SynchronizeAccessAsync("tenant-a", "linked-user", Employee.RoleManager, true, default), Times.Exactly(2));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task MissingEmployeeBeforeOrAfterLockReturnsNotFound(bool afterLock)
    {
        if (afterLock) _employees.Setup(x => x.ReloadAsync(_employee.Id, default)).ReturnsAsync((Employee?)null);
        else _employees.Setup(x => x.GetByIdAsync(_employee.Id, default)).ReturnsAsync((Employee?)null);
        await Assert.ThrowsAsync<NotFoundException>(() => Handler().Handle(new(_employee.Id), default));
        _employees.Verify(x => x.SaveAsync(It.IsAny<Employee>(), default), Times.Never);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CreateRejectsDuplicateIncludingInactiveWithNormalizedInput(bool active)
    {
        _employee.IsActive = active;
        _employees.Setup(x => x.GetByEmailAsync("cleaner@example.invalid", default)).ReturnsAsync(_employee);
        var fields = new EmployeeFields("  cleaner@example.invalid  ", "First", "Last", "", "employee", null, new(), null, null, null, null, null);
        var error = await Assert.ThrowsAsync<EmployeeIdentityConflictException>(() => new CreateEmployeeHandler(_employees.Object).Handle(new(fields), default));
        if (!active) { Assert.Contains("Inactive tab", error.Message); Assert.Contains("Reactivate", error.Message); }
        _employees.Verify(x => x.SaveAsync(It.IsAny<Employee>(), default), Times.Never);
    }

    [Fact]
    public async Task UpdateCannotMoveAnUnlinkedProfileOntoAnotherEmployeesEmail()
    {
        var existing = new Employee { Email = "other@example.invalid", IsActive = false };
        _employees.Setup(x => x.GetByEmailAsync(existing.Email, default)).ReturnsAsync(existing);
        var fields = new EmployeeFields(existing.Email, "First", "Last", "", "employee", null, new(), null, null, null, null, null);
        await Assert.ThrowsAsync<EmployeeIdentityConflictException>(() => new UpdateEmployeeHandler(_employees.Object, _identity.Object, _tenant.Object, _locks.Object).Handle(new(_employee.Id, fields, true), default));
        _employees.Verify(x => x.SaveAsync(It.IsAny<Employee>(), default), Times.Never);
    }
}

public class EmployeeReactivationAuthorizationTests
{
    [Fact]
    public void ReactivationEndpointRequiresTenantAdmin()
    {
        var method = typeof(CleaningSuite.Api.Controllers.EmployeesAdminController).GetMethod("Reactivate")!;
        var authorization = method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>().Single();
        Assert.Equal(CleaningSuite.Api.Auth.RealmRoleAuthorization.PolicyPrefix + "admin", authorization.Policy);
        var route = method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.HttpPostAttribute), false)
            .Cast<Microsoft.AspNetCore.Mvc.HttpPostAttribute>().Single();
        Assert.Equal("{id:guid}/reactivate", route.Template);
    }
}
