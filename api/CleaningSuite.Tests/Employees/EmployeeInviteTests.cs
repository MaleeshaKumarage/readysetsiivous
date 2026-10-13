using CleaningSuite.Application.Common;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.Employees.Commands;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Employees;
using Microsoft.Extensions.Configuration;
using Moq;

namespace CleaningSuite.Tests.Employees;
public class EmployeeInviteTests
{
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IEmployeeIdentityProvider> _identity = new();
    private readonly Mock<IEmployeeIdentityLock> _locks = new();
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Employee _employee = new() { Email = "cleaner@example.invalid", FirstName = "Test", LastName = "Cleaner" };
    private static readonly DateTime Now = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Lease : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    public EmployeeInviteTests() {
        _tenant.SetupGet(x => x.TenantId).Returns("tenant-a");
        _employees.Setup(x => x.GetByIdAsync(_employee.Id, default)).ReturnsAsync(_employee);
        _employees.Setup(x => x.ReloadAsync(_employee.Id, default)).ReturnsAsync(_employee);
        _identity.Setup(x => x.EnsureAsync("tenant-a", _employee, default)).ReturnsAsync(new EmployeeIdentity("user-a"));
        _locks.Setup(x => x.AcquireAsync("tenant-a", _employee.Email, default)).ReturnsAsync(new Lease());
    }
    private InviteEmployeeHandler Handler(string? frontend = "https://dev.example.invalid", string? keycloak = "https://auth.example.invalid") =>
        new(_employees.Object, _tenant.Object, _identity.Object, _locks.Object, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["App:FrontendBaseUrl"] = frontend, ["Auth:Keycloak:ServerUrl"] = keycloak
        }).Build(), new Clock());
    [Fact]
    public async Task InvitationBindsBeforeNativeEmailAndHasCorrectTenantLanding()
    {
        _identity.Setup(x => x.SendSetupEmailAsync("tenant-a", "user-a", "https://dev.example.invalid/account/?tenant=tenant-a", 86400, default))
            .Callback(() => { Assert.Equal("user-a", _employee.KeycloakUserId); _employees.Verify(x => x.SaveAsync(_employee, default), Times.Once); }).Returns(Task.CompletedTask);
        var result = await Handler().Handle(new(_employee.Id), default);
        Assert.Equal("Sent", result.Status); Assert.Equal(Now, result.SentUtc);
        Assert.Null(_employee.InviteError);
        _employees.Verify(x => x.SaveAsync(_employee, default), Times.Exactly(2));
    }
    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("javascript:alert(1)")] [InlineData("https://site.invalid/fi/admin/")]
    [InlineData("https://site.invalid/?redirect=https://evil.invalid")] [InlineData("https://user:password@site.invalid")]
    public async Task InvalidFrontendFailsBeforeIdentityCreation(string? frontend)
    {
        await Assert.ThrowsAsync<EmployeeInviteConfigurationException>(() => Handler(frontend).Handle(new(_employee.Id), default));
        _identity.Verify(x => x.EnsureAsync(It.IsAny<string>(), It.IsAny<Employee>(), default), Times.Never);
    }
    [Fact]
    public async Task MissingKeycloakUrlDoesNotSendOrBind() {
        await Assert.ThrowsAsync<EmployeeInviteConfigurationException>(() => Handler(keycloak: null).Handle(new(_employee.Id), default));
        _employees.Verify(x => x.SaveAsync(It.IsAny<Employee>(), default), Times.Never);
    }
    [Fact]
    public async Task InactiveEmployeeCannotBeInvited() {
        _employee.IsActive = false;
        await Assert.ThrowsAsync<EmployeeIdentityConflictException>(() => Handler().Handle(new(_employee.Id), default));
        _identity.Verify(x => x.EnsureAsync(It.IsAny<string>(), It.IsAny<Employee>(), default), Times.Never);
    }
    [Fact]
    public async Task FailedDeliveryRetainsBindingAndRetryClearsError() {
        _identity.SetupSequence(x => x.SendSetupEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), default))
            .ThrowsAsync(new EmployeeIdentityProviderException("Mail failed")).Returns(Task.CompletedTask);
        await Assert.ThrowsAsync<EmployeeIdentityProviderException>(() => Handler().Handle(new(_employee.Id), default));
        Assert.Equal("user-a", _employee.KeycloakUserId); Assert.NotNull(_employee.InviteError); Assert.Null(_employee.InviteLastSentUtc);
        var result = await Handler().Handle(new(_employee.Id), default);
        Assert.Equal("Sent", result.Status); Assert.Null(_employee.InviteError);
    }
    [Fact]
    public async Task ImmediateDoubleClickReturnsPriorSendWithoutAnotherEmail() {
        _employee.KeycloakUserId = "user-a"; _employee.InviteLastSentUtc = Now.AddSeconds(-10);
        var result = await Handler().Handle(new(_employee.Id), default);
        Assert.Equal("AlreadySent", result.Status);
        _identity.Verify(x => x.SendSetupEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), default), Times.Never);
    }
    [Fact]
    public async Task IdentityAlreadyLinkedElsewhereIsNotSent() {
        _employees.Setup(x => x.GetByKeycloakUserIdAsync("user-a", default)).ReturnsAsync(new Employee { Id = Guid.NewGuid() });
        await Assert.ThrowsAsync<EmployeeIdentityConflictException>(() => Handler().Handle(new(_employee.Id), default));
        _employees.Verify(x => x.SaveAsync(It.IsAny<Employee>(), default), Times.Never);
    }
    [Fact]
    public async Task DatabaseBindingFailureCannotSendAnUntrackedInvitation() {
        _employees.Setup(x => x.SaveAsync(_employee, default)).ThrowsAsync(new JasperFx.ConcurrencyException("test"));
        await Assert.ThrowsAsync<JasperFx.ConcurrencyException>(() => Handler().Handle(new(_employee.Id), default));
        _identity.Verify(x => x.SendSetupEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), default), Times.Never);
        _identity.Verify(x => x.AssignRoleAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }
    [Fact]
    public async Task DeactivationSavesLocalGuardBeforeDisablingIdentity() {
        _employee.KeycloakUserId = "user-a";
        _identity.Setup(x => x.SynchronizeAccessAsync("tenant-a", "user-a", "employee", false, default)).Callback(() => {
            Assert.False(_employee.IsActive); _employees.Verify(x => x.SaveAsync(_employee, default), Times.Once);
        }).Returns(Task.CompletedTask);
        await new DeactivateEmployeeHandler(_employees.Object, _identity.Object, _tenant.Object, _locks.Object).Handle(new(_employee.Id), default);
        _identity.Verify(x => x.SynchronizeAccessAsync("tenant-a", "user-a", "employee", false, default), Times.Once);
    }
    [Fact]
    public async Task FailedRoleSynchronizationRemainsPendingAndSameUpdateCanRecover()
    {
        _employee.KeycloakUserId = "user-a";
        _identity.SetupSequence(x => x.SynchronizeAccessAsync("tenant-a", "user-a", "admin", true, default))
            .ThrowsAsync(new EmployeeIdentityProviderException("Provider failed")).Returns(Task.CompletedTask);
        var handler = new UpdateEmployeeHandler(_employees.Object, _identity.Object, _tenant.Object, _locks.Object);
        var command = new UpdateEmployeeCommand(_employee.Id, Fields("admin"), true);
        await Assert.ThrowsAsync<EmployeeIdentityProviderException>(() => handler.Handle(command, default));
        Assert.Equal("admin", _employee.Role); Assert.True(_employee.IdentitySyncPending);
        await handler.Handle(command, default);
        Assert.False(_employee.IdentitySyncPending);
        _identity.Verify(x => x.SynchronizeAccessAsync("tenant-a", "user-a", "admin", true, default), Times.Exactly(2));
    }
    [Fact]
    public async Task UnrelatedProfileEditsDoNotChangeSessionsOrPasswords()
    {
        _employee.KeycloakUserId = "user-a";
        await new UpdateEmployeeHandler(_employees.Object, _identity.Object, _tenant.Object, _locks.Object).Handle(new(_employee.Id, Fields("employee"), true), default);
        _identity.Verify(x => x.SynchronizeAccessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), default), Times.Never);
    }
    [Fact]
    public async Task BoundEmailCannotBeSilentlyChanged()
    {
        _employee.KeycloakUserId = "user-a";
        await Assert.ThrowsAsync<EmployeeIdentityConflictException>(() => new UpdateEmployeeHandler(_employees.Object, _identity.Object, _tenant.Object, _locks.Object)
            .Handle(new(_employee.Id, Fields("employee") with { Email = "other@example.invalid" }, true), default));
        _employees.Verify(x => x.SaveAsync(It.IsAny<Employee>(), default), Times.Never);
    }
    private EmployeeFields Fields(string role) => new(_employee.Email, _employee.FirstName, _employee.LastName, "", role, null, new(), null, null, null, null, null);
    [Fact]
    public async Task RoleIsGrantedOnlyAfterBindingIsPersisted()
    {
        _employee.Role = "admin";
        _identity.Setup(x => x.AssignRoleAsync("tenant-a", "user-a", "admin", default)).Callback(() => {
            _employees.Verify(x => x.SaveAsync(_employee, default), Times.Once);
            Assert.Equal("user-a", _employee.KeycloakUserId);
        }).Returns(Task.CompletedTask);
        await Handler().Handle(new(_employee.Id), default);
        _identity.Verify(x => x.AssignRoleAsync("tenant-a", "user-a", "admin", default), Times.Once);
    }
    [Fact]
    public async Task DuplicateEmployeeEmailCannotClaimAnExistingLogin()
    {
        _employees.Setup(x => x.GetByEmailAsync(_employee.Email, default)).ReturnsAsync(new Employee { Id = Guid.NewGuid(), Email = _employee.Email });
        await Assert.ThrowsAsync<EmployeeIdentityConflictException>(() => Handler().Handle(new(_employee.Id), default));
        _identity.Verify(x => x.EnsureAsync(It.IsAny<string>(), It.IsAny<Employee>(), default), Times.Never);
    }
}
