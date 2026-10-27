using CleaningSuite.Domain.Employees;
namespace CleaningSuite.Application.Employees;

public record EmployeeIdentity(string UserId);
public interface IEmployeeIdentityProvider
{
    Task ValidateInvitationAsync(string tenantId, string redirectUri, string role, CancellationToken ct = default);
    Task<EmployeeIdentity> EnsureAsync(string tenantId, Employee employee, CancellationToken ct = default);
    Task AssignRoleAsync(string tenantId, Guid employeeId, string userId, string role, CancellationToken ct = default);
    Task SendSetupEmailAsync(string tenantId, Guid employeeId, string userId, string redirectUri, int lifespanSeconds, CancellationToken ct = default);
    Task SynchronizeAccessAsync(string tenantId, Guid employeeId, string userId, string role, bool enabled, CancellationToken ct = default);
}
public interface IEmployeeIdentityLock
{
    Task<IAsyncDisposable> AcquireAsync(string tenantId, string email, CancellationToken ct = default);
}
public class EmployeeIdentityConflictException(string message) : Exception(message);
public class EmployeeInviteConfigurationException(string message) : Exception(message);
public class EmployeeIdentityProviderException(string message, Exception? inner = null) : Exception(message, inner);
