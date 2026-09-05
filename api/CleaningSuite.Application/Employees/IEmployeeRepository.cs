using CleaningSuite.Domain.Employees;

namespace CleaningSuite.Application.Employees;

public interface IEmployeeRepository
{
    Task<IReadOnlyList<Employee>> ListAsync(bool includeInactive, CancellationToken ct = default);
    Task<Employee?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Employee?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<Employee?> GetByKeycloakUserIdAsync(string keycloakUserId, CancellationToken ct = default);
    Task SaveAsync(Employee employee, CancellationToken ct = default);
}
