using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Common;
using CleaningSuite.Domain.QualityCycle;
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

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        // Remove the live shift and roster atomically; keep a separate snapshot
        // so completed QC evidence and historic reports remain meaningful.
        await _tenantSession.BeginReservationAsync(ct);
        var shift = await Session.LoadAsync<Shift>(id, ct) ?? throw new NotFoundException("Shift", id);
        if (shift.IsActive) throw new InactiveRecordDeleteConflictException("Deactivate the shift before deleting it.");
        var assignments = await Session.Query<ShiftAssignment>().Where(x => x.ShiftId == id).ToListAsync(ct);
        var archive = new DeletedShift { Id = shift.Id, Snapshot = shift, Assignments = assignments.ToList() };
        foreach (var assignment in assignments) Session.Delete<ShiftAssignment>(assignment.Id);
        var exceptions = await Session.Query<ShiftOccurrenceException>().Where(x => x.ShiftId == id).ToListAsync(ct);
        archive.OccurrenceExceptions = exceptions.ToList();
        var rosterOverrides = await Session.Query<ShiftOccurrenceAssignment>().Where(x => x.ShiftId == id).ToListAsync(ct);
        archive.OccurrenceAssignments = rosterOverrides.ToList();
        foreach (var item in rosterOverrides) Session.Delete<ShiftOccurrenceAssignment>(item.Id);
        Session.Store(archive);
        foreach (var exception in exceptions) Session.Delete<ShiftOccurrenceException>(exception.Id);
        var forms = await Session.Query<QualityCycleForm>().Where(x => x.ShiftId == id).ToListAsync(ct);
        foreach (var form in forms)
        {
            form.ShiftDeletedUtc = DateTime.UtcNow;
            Session.Store(form);
        }
        Session.Delete<Shift>(id);
        await Session.SaveChangesAsync(ct);
    }

    public async Task<Shift?> GetDeletedSnapshotAsync(Guid id, CancellationToken ct = default) =>
        (await Session.LoadAsync<DeletedShift>(id, ct))?.Snapshot;

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

    public async Task<IReadOnlyList<ShiftAssignment>> ListAssignmentsByShiftAsync(
        Guid shiftId,
        CancellationToken ct = default)
    {
        return await Session.Query<ShiftAssignment>()
            .Where(x => x.ShiftId == shiftId && x.IsActive)
            .ToListAsync(ct);
    }

    public async Task SaveAssignmentAsync(ShiftAssignment assignment, CancellationToken ct = default)
    {
        Session.Store(assignment);
        await Session.SaveChangesAsync(ct);
    }

    public Task<ShiftOccurrenceAssignment?> GetOccurrenceAssignmentAsync(Guid id, CancellationToken ct = default) =>
        Session.LoadAsync<ShiftOccurrenceAssignment>(id, ct);

    public async Task<IReadOnlyList<ShiftOccurrenceAssignment>> ListOccurrenceAssignmentsAsync(Guid shiftId, CancellationToken ct = default)
    {
        var archive = await Session.LoadAsync<DeletedShift>(shiftId, ct);
        return archive is not null ? archive.OccurrenceAssignments
            : await Session.Query<ShiftOccurrenceAssignment>().Where(x => x.ShiftId == shiftId).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ShiftOccurrenceAssignment>> ListOccurrenceAssignmentsByEmployeeAsync(Guid employeeId, CancellationToken ct = default) =>
        await Session.Query<ShiftOccurrenceAssignment>().Where(x => x.Mode != OccurrenceAssignmentMode.Inherit && x.EmployeeIds.Contains(employeeId)).ToListAsync(ct);

    public async Task SaveOccurrenceAssignmentAsync(ShiftOccurrenceAssignment assignment, CancellationToken ct = default)
    { Session.Store(assignment); await Session.SaveChangesAsync(ct); }

    public async Task SaveAssignmentPeriodsAsync(IReadOnlyList<ShiftAssignment> assignments, CancellationToken ct = default)
    { foreach (var assignment in assignments) Session.Store(assignment); await Session.SaveChangesAsync(ct); }

    public async Task DeleteAssignmentAsync(Guid assignmentId, CancellationToken ct = default)
    {
        Session.Delete<ShiftAssignment>(assignmentId);
        await Session.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<Shift>> ListIncludingInactiveAsync(CancellationToken ct = default) =>
        await Session.Query<Shift>().OrderBy(s => s.Name).ToListAsync(ct);

    public async Task<IReadOnlyList<Shift>> ListForReportingAsync(CancellationToken ct = default)
    {
        var live = await Session.Query<Shift>().ToListAsync(ct);
        var deleted = await Session.Query<DeletedShift>().ToListAsync(ct);
        return live.Concat(deleted.Select(x => x.Snapshot)).OrderBy(x => x.Name).ToList();
    }

    public async Task<IReadOnlyList<ShiftAssignment>> ListAssignmentHistoryAsync(Guid shiftId, CancellationToken ct = default)
    {
        var archive = await Session.LoadAsync<DeletedShift>(shiftId, ct);
        return archive is not null ? archive.Assignments
            : await Session.Query<ShiftAssignment>().Where(a => a.ShiftId == shiftId).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ShiftAssignment>> ListAssignmentHistoryByEmployeeAsync(Guid employeeId, CancellationToken ct = default) =>
        await Session.Query<ShiftAssignment>().Where(a => a.EmployeeId == employeeId).ToListAsync(ct);

    public Task<ShiftOccurrenceException?> GetOccurrenceExceptionAsync(Guid id, CancellationToken ct = default) =>
        Session.LoadAsync<ShiftOccurrenceException>(id, ct);

    public Task<ShiftOccurrenceException?> GetOccurrenceExceptionByOriginalAsync(Guid shiftId, DateTime originalStartUtc, CancellationToken ct = default)
    {
        // Marten's JSON DateTime predicates use PostgreSQL timestamp without time
        // zone. Preserve UTC clock values, but use Unspecified query parameters,
        // matching BookingRepository and QualityCycleRepository. Stored values
        // remain UTC; this is only the database query boundary.
        var start = DateTime.SpecifyKind(originalStartUtc, DateTimeKind.Unspecified);
        return Session.Query<ShiftOccurrenceException>()
            .FirstOrDefaultAsync(x => x.ShiftId == shiftId && x.OriginalStartUtc == start, ct);
    }

    public async Task<IReadOnlyList<ShiftOccurrenceException>> ListOccurrenceExceptionsAsync(Guid shiftId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        var archive = await Session.LoadAsync<DeletedShift>(shiftId, ct);
        if (archive is not null) return archive.OccurrenceExceptions.Where(x =>
            (x.StartUtc >= fromUtc && x.StartUtc < toUtc)
            || (x.OriginalStartUtc >= fromUtc && x.OriginalStartUtc < toUtc)).ToList();
        var from = DateTime.SpecifyKind(fromUtc, DateTimeKind.Unspecified);
        var to = DateTime.SpecifyKind(toUtc, DateTimeKind.Unspecified);
        return await Session.Query<ShiftOccurrenceException>()
            .Where(x => x.ShiftId == shiftId && ((x.StartUtc >= from && x.StartUtc < to)
                || (x.OriginalStartUtc != null && x.OriginalStartUtc >= from && x.OriginalStartUtc < to)))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ShiftOccurrenceException>> ListOccurrenceExceptionsByEmployeeAsync(Guid employeeId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        var from = DateTime.SpecifyKind(fromUtc, DateTimeKind.Unspecified);
        var to = DateTime.SpecifyKind(toUtc, DateTimeKind.Unspecified);
        return await Session.Query<ShiftOccurrenceException>()
            .Where(x => x.EmployeeId == employeeId && x.StartUtc >= from && x.StartUtc < to)
            .ToListAsync(ct);
    }

    public async Task SaveOccurrenceExceptionAsync(ShiftOccurrenceException exception, CancellationToken ct = default)
    { Session.Store(exception); await Session.SaveChangesAsync(ct); }

    public async Task DeleteOccurrenceExceptionAsync(Guid id, CancellationToken ct = default)
    { Session.Delete<ShiftOccurrenceException>(id); await Session.SaveChangesAsync(ct); }
}
