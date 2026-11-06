using CleaningSuite.Application.Notifications;
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
    private readonly IPushOutbox? _push;
    private readonly Dictionary<Guid, string> _shiftSnapshots = new();
    private readonly Dictionary<Guid, ShiftOccurrenceAssignment> _overrideRecipients = new();
    private readonly Dictionary<Guid, Guid[]> _recurringRecipients = new();
    private readonly Dictionary<Guid, ShiftOccurrenceException> _exceptionRecipients = new();

    public ShiftRepository(ITenantSession tenantSession, IPushOutbox? push = null)
    {
        _tenantSession = tenantSession;
        _push = push;
    }

    private IDocumentSession Session => _tenantSession.Session;

    public async Task<Shift?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var shift = await Session.LoadAsync<Shift>(id, ct);
        if (shift is not null) _shiftSnapshots.TryAdd(id, ScheduleFingerprint(shift));
        return shift;
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
        if (!_shiftSnapshots.TryGetValue(shift.Id, out var before) || before != ScheduleFingerprint(shift))
            await StageChange(shift.Id, [], ct);
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
        await StageChange(id, assignments.Select(x => x.EmployeeId), ct);
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

    public async Task<IReadOnlyDictionary<Guid, int>> CountActiveAssignmentsByShiftAsync(CancellationToken ct = default)
    {
        // The session is tenant-scoped (all documents are multi-tenanted), so this never sees another tenant's rows.
        // Only the shift id column is read, not whole assignment documents.
        var shiftIds = await Session.Query<ShiftAssignment>()
            .Where(x => x.IsActive)
            .Select(x => x.ShiftId)
            .ToListAsync(ct);
        return shiftIds.GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());
    }

    public async Task SaveAssignmentAsync(ShiftAssignment assignment, CancellationToken ct = default)
    {
        await StageChange(assignment.ShiftId, [assignment.EmployeeId], ct);
        Session.Store(assignment);
        await Session.SaveChangesAsync(ct);
    }

    public async Task SaveDeactivationAsync(Shift shift, IReadOnlyList<ShiftAssignment> releasedAssignments, CancellationToken ct = default)
    {
        foreach (var assignment in releasedAssignments)
            await StageChange(assignment.ShiftId, [assignment.EmployeeId], ct);
        if (!_shiftSnapshots.TryGetValue(shift.Id, out var before) || before != ScheduleFingerprint(shift))
            await StageChange(shift.Id, [], ct);
        Session.Store(shift);
        foreach (var assignment in releasedAssignments) Session.Store(assignment);
        await Session.SaveChangesAsync(ct);
    }

    public async Task<ShiftOccurrenceAssignment?> GetOccurrenceAssignmentAsync(Guid id, CancellationToken ct = default)
    {
        var item = await Session.LoadAsync<ShiftOccurrenceAssignment>(id, ct);
        if (item is not null) _overrideRecipients.TryAdd(id, Snapshot(item));
        return item;
    }

    public async Task<IReadOnlyList<ShiftOccurrenceAssignment>> ListOccurrenceAssignmentsAsync(Guid shiftId, CancellationToken ct = default)
    {
        var archive = await Session.LoadAsync<DeletedShift>(shiftId, ct);
        var items = archive is not null ? archive.OccurrenceAssignments
            : await Session.Query<ShiftOccurrenceAssignment>().Where(x => x.ShiftId == shiftId).ToListAsync(ct);
        foreach (var item in items) _overrideRecipients.TryAdd(item.Id, Snapshot(item));
        return items;
    }

    public async Task<IReadOnlyList<ShiftOccurrenceAssignment>> ListOccurrenceAssignmentsByEmployeeAsync(Guid employeeId, CancellationToken ct = default) =>
        await Session.Query<ShiftOccurrenceAssignment>().Where(x => x.Mode != OccurrenceAssignmentMode.Inherit && x.EmployeeIds.Contains(employeeId)).ToListAsync(ct);

    public async Task SaveOccurrenceAssignmentAsync(ShiftOccurrenceAssignment assignment, CancellationToken ct = default)
    { await StageVisitChange(assignment.ShiftId, assignment.OriginalStartUtc, assignment.ExtraExceptionId, _overrideRecipients.GetValueOrDefault(assignment.Id), assignment, null, null, ct); Session.Store(assignment); await Session.SaveChangesAsync(ct); }

    public async Task SaveAssignmentPeriodsAsync(IReadOnlyList<ShiftAssignment> assignments, CancellationToken ct = default)
    { foreach (var group in assignments.GroupBy(x => x.ShiftId)) await StageChange(group.Key, group.Where(x => x.IsActive || x.Periods.Any(p => !p.ToUtc.HasValue || p.ToUtc > DateTime.UtcNow)).Select(x => x.EmployeeId).Concat(_recurringRecipients.GetValueOrDefault(group.Key) ?? []), ct); foreach (var assignment in assignments) Session.Store(assignment); await Session.SaveChangesAsync(ct); }

    public async Task DeleteAssignmentAsync(Guid assignmentId, CancellationToken ct = default)
    {
        var old = await Session.LoadAsync<ShiftAssignment>(assignmentId, ct);
        if (old is not null) await StageChange(old.ShiftId, [old.EmployeeId], ct);
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
        var items = archive is not null ? archive.Assignments
            : await Session.Query<ShiftAssignment>().Where(a => a.ShiftId == shiftId).ToListAsync(ct);
        _recurringRecipients.TryAdd(shiftId, items.Where(x => x.IsActive || x.Periods.Any(p => !p.ToUtc.HasValue || p.ToUtc > DateTime.UtcNow)).Select(x => x.EmployeeId).ToArray());
        return items;
    }

    public async Task<IReadOnlyList<ShiftAssignment>> ListAssignmentHistoryByEmployeeAsync(Guid employeeId, CancellationToken ct = default) =>
        await Session.Query<ShiftAssignment>().Where(a => a.EmployeeId == employeeId).ToListAsync(ct);

    public async Task<ShiftOccurrenceException?> GetOccurrenceExceptionAsync(Guid id, CancellationToken ct = default)
    {
        var item = await Session.LoadAsync<ShiftOccurrenceException>(id, ct);
        if (item is not null) _exceptionRecipients.TryAdd(id, Snapshot(item));
        return item;
    }

    public async Task<ShiftOccurrenceException?> GetOccurrenceExceptionByOriginalAsync(Guid shiftId, DateTime originalStartUtc, CancellationToken ct = default)
    {
        // Marten's JSON DateTime predicates use PostgreSQL timestamp without time
        // zone. Preserve UTC clock values, but use Unspecified query parameters,
        // matching BookingRepository and QualityCycleRepository. Stored values
        // remain UTC; this is only the database query boundary.
        var start = DateTime.SpecifyKind(originalStartUtc, DateTimeKind.Unspecified);
        var item = await Session.Query<ShiftOccurrenceException>()
            .FirstOrDefaultAsync(x => x.ShiftId == shiftId && x.OriginalStartUtc == start, ct);
        if (item is not null) _exceptionRecipients.TryAdd(item.Id, Snapshot(item));
        return item;
    }

    public async Task<ShiftRosterInputs> ListRosterInputsAsync(IReadOnlyCollection<Guid> shiftIds, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        var ids = shiftIds.Distinct().ToArray();
        if (ids.Length == 0) return new(new Dictionary<Guid, IReadOnlyList<ShiftOccurrenceException>>(), new Dictionary<Guid, IReadOnlyList<ShiftAssignment>>(), new Dictionary<Guid, IReadOnlyList<ShiftOccurrenceAssignment>>());
        // Same occurrence-window rule as ListOccurrenceExceptionsAsync, with Unspecified-kind bounds for the stored columns.
        var from = DateTime.SpecifyKind(fromUtc, DateTimeKind.Unspecified);
        var to = DateTime.SpecifyKind(toUtc, DateTimeKind.Unspecified);
        var exceptions = (await Session.Query<ShiftOccurrenceException>()
            .Where(x => ids.Contains(x.ShiftId) && ((x.StartUtc >= from && x.StartUtc < to)
                || (x.OriginalStartUtc != null && x.OriginalStartUtc >= from && x.OriginalStartUtc < to)))
            .ToListAsync(ct)).GroupBy(x => x.ShiftId).ToDictionary(g => g.Key, g => (IReadOnlyList<ShiftOccurrenceException>)g.ToList());
        var history = (await Session.Query<ShiftAssignment>().Where(a => ids.Contains(a.ShiftId)).ToListAsync(ct))
            .GroupBy(a => a.ShiftId).ToDictionary(g => g.Key, g => (IReadOnlyList<ShiftAssignment>)g.ToList());
        var overrides = (await Session.Query<ShiftOccurrenceAssignment>().Where(x => ids.Contains(x.ShiftId)).ToListAsync(ct))
            .GroupBy(x => x.ShiftId).ToDictionary(g => g.Key, g => (IReadOnlyList<ShiftOccurrenceAssignment>)g.ToList());
        return new(exceptions, history, overrides);
    }

    public async Task<IReadOnlyList<ShiftOccurrenceException>> ListOccurrenceExceptionsAsync(Guid shiftId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        var archive = await Session.LoadAsync<DeletedShift>(shiftId, ct);
        if (archive is not null) return archive.OccurrenceExceptions.Where(x =>
            (x.StartUtc >= fromUtc && x.StartUtc < toUtc)
            || (x.OriginalStartUtc >= fromUtc && x.OriginalStartUtc < toUtc)).ToList();
        var from = DateTime.SpecifyKind(fromUtc, DateTimeKind.Unspecified);
        var to = DateTime.SpecifyKind(toUtc, DateTimeKind.Unspecified);
        var items = await Session.Query<ShiftOccurrenceException>()
            .Where(x => x.ShiftId == shiftId && ((x.StartUtc >= from && x.StartUtc < to)
                || (x.OriginalStartUtc != null && x.OriginalStartUtc >= from && x.OriginalStartUtc < to)))
            .ToListAsync(ct);
        foreach (var item in items) _exceptionRecipients.TryAdd(item.Id, Snapshot(item));
        return items;
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
    { await StageVisitChange(exception.ShiftId, exception.OriginalStartUtc, exception.OriginalStartUtc.HasValue ? null : exception.Id, null, null, _exceptionRecipients.GetValueOrDefault(exception.Id), exception, ct); Session.Store(exception); await Session.SaveChangesAsync(ct); }

    public async Task DeleteOccurrenceExceptionAsync(Guid id, CancellationToken ct = default)
    { var old = await Session.LoadAsync<ShiftOccurrenceException>(id, ct); if (old is not null) await StageVisitChange(old.ShiftId, old.OriginalStartUtc, old.OriginalStartUtc.HasValue ? null : old.Id, null, null, old, null, ct); Session.Delete<ShiftOccurrenceException>(id); await Session.SaveChangesAsync(ct); }
    private static T Snapshot<T>(T value) => System.Text.Json.JsonSerializer.Deserialize<T>(System.Text.Json.JsonSerializer.Serialize(value))!;

    private async Task StageVisitChange(Guid shiftId, DateTime? original, Guid? extra,
        ShiftOccurrenceAssignment? beforeRoster, ShiftOccurrenceAssignment? afterRoster,
        ShiftOccurrenceException? beforeException, ShiftOccurrenceException? afterException, CancellationToken ct)
    {
        if (_push is null) return;
        var shift = await Session.LoadAsync<Shift>(shiftId, ct);
        if (shift is null) return;
        var profile = await Session.Query<CleaningSuite.Domain.Tenants.TenantProfile>().FirstOrDefaultAsync(ct);
        var zone = TenantTimeZone.Resolve(profile?.TimeZoneId);
        var anchor = original ?? beforeException?.StartUtc ?? afterException?.StartUtc;
        if (!anchor.HasValue && extra.HasValue)
            anchor = (await Session.LoadAsync<ShiftOccurrenceException>(extra.Value, ct))?.StartUtc;
        if (!anchor.HasValue) return;
        var day = TenantTimeZone.LocalDate(anchor.Value, zone);
        var generated = ShiftScheduleCalculator.GenerateOccurrences(shift, day.AddDays(-1), day.AddDays(1), zone)
            .Where(x => original.HasValue && x.StartUtc == original.Value).ToArray();
        var assignments = await Session.Query<ShiftAssignment>().Where(x => x.ShiftId == shiftId).ToListAsync(ct);
        var overrides = await Session.Query<ShiftOccurrenceAssignment>().Where(x => x.ShiftId == shiftId).ToListAsync(ct);
        var exceptions = await Session.Query<ShiftOccurrenceException>().Where(x => x.ShiftId == shiftId).ToListAsync(ct);
        var overrideId = ShiftOccurrenceAssignment.Identity(shiftId, original, extra);
        var changedRoster = afterRoster is not null;
        var changedException = beforeException is not null || afterException is not null;
        IReadOnlyList<Guid> Resolve(bool after)
        {
            var roster = changedRoster ? overrides.Where(x => x.Id != overrideId).Concat(
                (after ? afterRoster : beforeRoster) is { } rosterItem ? [rosterItem] : []) : overrides;
            var exceptionId = beforeException?.Id ?? afterException?.Id;
            var changes = changedException ? exceptions.Where(x => x.Id != exceptionId).Concat(
                (after ? afterException : beforeException) is { } exceptionItem ? [exceptionItem] : []) : exceptions;
            return ShiftVisitRoster.Resolve(shiftId, generated, changes, assignments, roster,
                legacyStartUtc: shift.ValidFrom.HasValue ? TenantTimeZone.StartOfLocalDay(shift.ValidFrom.Value.Date, zone) : null)
                .Where(x => x.OriginalStartUtc == original && x.ExtraExceptionId == extra)
                .SelectMany(x => x.EmployeeIds).Distinct().ToArray();
        }
        await _push.StageScheduleChangeAsync(Session, shiftId, Resolve(false).Concat(Resolve(true)), DateTimeOffset.UtcNow, ct);
    }

    private async Task StageChange(Guid shiftId, IEnumerable<Guid> additional, CancellationToken ct)
    {
        if (_push is null) return;
        // Read committed recipients before staging the mutation so removed employees retain notice.
        var assignments = await Session.Query<ShiftAssignment>().Where(x => x.ShiftId == shiftId && x.IsActive).ToListAsync(ct);
        var overrides = await Session.Query<ShiftOccurrenceAssignment>().Where(x => x.ShiftId == shiftId).ToListAsync(ct);
        var exceptions = await Session.Query<ShiftOccurrenceException>().Where(x => x.ShiftId == shiftId).ToListAsync(ct);
        var recipients = FutureRecipients(assignments, overrides, exceptions, DateTime.UtcNow).Concat(additional).Distinct();
        await _push.StageScheduleChangeAsync(Session, shiftId, recipients, DateTimeOffset.UtcNow, ct);
    }
    public static IReadOnlyList<Guid> FutureRecipients(IEnumerable<ShiftAssignment> assignments,
        IEnumerable<ShiftOccurrenceAssignment> overrides, IEnumerable<ShiftOccurrenceException> exceptions, DateTime now)
    {
        var relevantExceptions = exceptions.Where(x => x.EndUtc >= now).ToList();
        var extraIds = relevantExceptions.Select(x => x.Id).ToHashSet();
        var movedOriginals = relevantExceptions.Where(x => x.OriginalStartUtc.HasValue).Select(x => x.OriginalStartUtc!.Value).ToHashSet();
        return assignments.Where(x => x.IsActive || x.Periods.Any(p => !p.ToUtc.HasValue || p.ToUtc > now)).Select(x => x.EmployeeId)
            .Concat(overrides.Where(x => x.Mode != OccurrenceAssignmentMode.Inherit &&
                (x.OriginalStartUtc >= now || x.OriginalStartUtc.HasValue && movedOriginals.Contains(x.OriginalStartUtc.Value)
                    || x.ExtraExceptionId.HasValue && extraIds.Contains(x.ExtraExceptionId.Value))).SelectMany(x => x.EmployeeIds))
            .Concat(relevantExceptions.Where(x => x.EmployeeId.HasValue).Select(x => x.EmployeeId!.Value)).Distinct().ToArray();
    }

    private static string ScheduleFingerprint(Shift shift) => System.Text.Json.JsonSerializer.Serialize(new {
        shift.Name, shift.CompanyId, shift.BranchId, shift.Schedule, shift.IsActive, shift.ValidFrom, shift.ValidUntil });
}
