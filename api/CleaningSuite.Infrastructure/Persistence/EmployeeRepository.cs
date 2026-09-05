using CleaningSuite.Application.Employees;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Infrastructure.Persistence;
using Marten;

namespace CleaningSuite.Infrastructure.Persistence;

public class EmployeeRepository : IEmployeeRepository
{
    private readonly ITenantSession _tenantSession;

    public EmployeeRepository(ITenantSession tenantSession) => _tenantSession = tenantSession;

    public async Task<IReadOnlyList<Employee>> ListAsync(bool includeInactive, CancellationToken ct = default)
    {
        IQueryable<Employee> query = _tenantSession.Session.Query<Employee>();
        if (!includeInactive)
            query = query.Where(e => e.IsActive);

        return await query.OrderBy(e => e.FirstName).ToListAsync(ct);
    }

    public Task<Employee?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _tenantSession.Session.LoadAsync<Employee>(id, ct);

    public async Task<Employee?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        var results = await _tenantSession.Session.Query<Employee>()
            .Where(e => e.Email == email).ToListAsync(ct);
        return results.FirstOrDefault();
    }

    public async Task<Employee?> GetByKeycloakUserIdAsync(string keycloakUserId, CancellationToken ct = default)
    {
        var results = await _tenantSession.Session.Query<Employee>()
            .Where(e => e.KeycloakUserId == keycloakUserId).ToListAsync(ct);
        return results.FirstOrDefault();
    }

    public async Task SaveAsync(Employee employee, CancellationToken ct = default)
    {
        _tenantSession.Session.Store(employee);
        await _tenantSession.Session.SaveChangesAsync(ct);
    }
}
