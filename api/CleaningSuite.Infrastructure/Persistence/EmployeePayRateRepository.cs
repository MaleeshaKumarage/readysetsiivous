using CleaningSuite.Application.Employees.PayRates;
using CleaningSuite.Domain.Employees;
using Marten;

namespace CleaningSuite.Infrastructure.Persistence;

public class EmployeePayRateRepository(ITenantSession tenantSession) : IEmployeePayRateRepository
{
    public async Task<IReadOnlyList<EmployeePayRate>> ListAsync(Guid employeeId, CancellationToken ct = default) =>
        await tenantSession.Session.Query<EmployeePayRate>()
            .Where(entry => entry.EmployeeId == employeeId)
            .OrderBy(entry => entry.EffectiveFrom)
            .ToListAsync(ct);

    public async Task AddAsync(EmployeePayRate entry, CancellationToken ct = default)
    {
        // Insert, not Store: a second rate for the same employee and date fails on the deterministic id.
        tenantSession.Session.Insert(entry);
        try { await tenantSession.Session.SaveChangesAsync(ct); }
        catch (Exception error) when (IsDuplicate(error))
        { throw new EmployeePayRateConflictException("A pay rate already starts on this date. Choose another date."); }
    }

    private static bool IsDuplicate(Exception error) =>
        error is JasperFx.DocumentAlreadyExistsException
        || error is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation }
        || (error.InnerException is { } inner && IsDuplicate(inner));
}
