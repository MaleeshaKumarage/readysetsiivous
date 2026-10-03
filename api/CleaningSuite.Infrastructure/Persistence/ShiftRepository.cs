using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Infrastructure.Persistence;
using Marten;

namespace CleaningSuite.Infrastructure.Persistence;

public class ShiftRepository : IShiftRepository
{
    private readonly ITenantSession _tenantSession;

    public ShiftRepository(ITenantSession tenantSession)
    {
        _tenantSession = tenantSession;
    }

    private IDocumentSession Session => _tenantSession.Session;

    public async Task<Shift?> GetAsync(Guid id, CancellationToken ct = default)
    {
        return await Session.LoadAsync<Shift>(id, ct);
    }

    public async Task<IReadOnlyList<Shift>> ListAsync(
        Guid? companyId,
        Guid? branchId,
        CancellationToken ct = default)
    {
        var query = Session.Query<Shift>().AsQueryable();
        if (companyId.HasValue)
            query = query.Where(s => s.CompanyId == companyId.Value);
        if (branchId.HasValue)
            query = query.Where(s => s.BranchId == branchId.Value);
        return await query.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Shift>> ListByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken ct = default)
    {
        if (ids is null || ids.Count == 0)
            return Array.Empty<Shift>();
        var idList = ids as IList<Guid> ?? ids.ToList();
        return await Session.Query<Shift>()
            .Where(s => idList.Contains(s.Id))
            .ToListAsync(ct);
    }

    public async Task SaveAsync(Shift shift, CancellationToken ct = default)
    {
        Session.Store(shift);
        await Session.SaveChangesAsync(ct);
    }

    public async Task<ShiftAssignment?> GetAssignmentAsync(
        Guid shiftId,
        Guid employeeId,
        CancellationToken ct = default)
    {
        return await Session.Query<ShiftAssignment>()
            .FirstOrDefaultAsync(x => x.ShiftId == shiftId && x.EmployeeId == employeeId && x.IsActive, ct);
    }

    public async Task<IReadOnlyList<ShiftAssignment>> ListAssignmentsByEmployeeAsync(
        Guid employeeId,
        CancellationToken ct = default)
    {
        return await Session.Query<ShiftAssignment>()
            .Where(x => x.EmployeeId == employeeId && x.IsActive)
            .ToListAsync(ct);
    }

    public async Task SaveAssignmentAsync(ShiftAssignment assignment, CancellationToken ct = default)
    {
        Session.Store(assignment);
        await Session.SaveChangesAsync(ct);
    }

    public async Task DeleteAssignmentAsync(Guid assignmentId, CancellationToken ct = default)
    {
        Session.Delete<ShiftAssignment>(assignmentId);
        await Session.SaveChangesAsync(ct);
    }
}
