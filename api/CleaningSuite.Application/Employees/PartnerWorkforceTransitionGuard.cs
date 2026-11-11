using CleaningSuite.Application.Partners;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Bookings;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Partners;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using Marten;

namespace CleaningSuite.Application.Employees;

/// <summary>Called under the scheduling reservation before staff access is replaced with customer access.</summary>
public sealed class PartnerWorkforceTransitionGuard(IPartnerStore store, IShiftRepository shifts,
    ITenantProfileRepository profiles, ITenantContext tenant, TimeProvider clock)
{
    public async Task EnsureNoOutstandingWorkAsync(Guid employeeId, CancellationToken ct)
    {
        var db = store.Session;
        var now = clock.GetUtcNow().UtcDateTime;
        var forms = await db.Query<QualityCycleForm>().Where(f => f.EmployeeId == employeeId && f.ShiftDeletedUtc == null).ToListAsync(ct);
        if (forms.Any(f => f.StartedAtUtc.HasValue && !f.IsSubmitted)) Deny();
        var employeeKey = employeeId.ToString();
        var bookings = await db.Query<Booking>().Where(b => b.EmployeeId == employeeKey).ToListAsync(ct);
        if (bookings.Any(b => b.Status is not (Booking.StatusCancelled or Booking.StatusCompleted))) Deny();
        // A key pickup or handover still waiting for completion is outstanding work for this person too.
        var keyRequests = await db.Query<PartnerKeyRequest>().Where(k => k.AssignedEmployeeId == employeeId && k.Status == PartnerKeyRequest.StatusRequested).ToListAsync(ct);
        if (keyRequests.Count > 0) Deny();
        var requests = await db.Query<PartnerCleaningRequest>()
            .Where(r => r.OfferedEmployeeId == employeeId || r.AcceptedEmployeeId == employeeId).ToListAsync(ct);
        if (requests.Any(r => r.Status == "Offered" && r.OfferedEmployeeId == employeeId
            || r.Status == "Accepted" && r.AcceptedEmployeeId == employeeId
                && !forms.Any(f => f.ShiftId == r.LinkedShiftId && f.ShiftOccurrenceUtc == (r.OccurrenceStartUtc ?? r.PlannedStartUtc) && f.IsSubmitted))) Deny();

        var assignments = await db.Query<ShiftAssignment>().Where(a => a.EmployeeId == employeeId).ToListAsync(ct);
        var overrides = await db.Query<ShiftOccurrenceAssignment>().Where(a => a.EmployeeIds.Contains(employeeId)).ToListAsync(ct);
        var exceptions = await db.Query<ShiftOccurrenceException>().Where(e => e.EmployeeId == employeeId).ToListAsync(ct);
        var ids = assignments.Select(a => a.ShiftId).Concat(overrides.Select(a => a.ShiftId)).Concat(exceptions.Select(e => e.ShiftId)).Distinct();
        var (_, zone) = await TenantTimeZone.LoadAsync(profiles, tenant, ct);
        foreach (var id in ids)
        {
            var shift = await shifts.GetAsync(id, ct);
            // Inactive shifts can be reactivated with retained rosters. They must not restore
            // a customer account as an assignee after a staff-to-partner role change.
            if (shift is null) continue;
            // A 63-day window contains every supported recurring cadence. Include future period/
            // contract boundaries and explicit visit dates so far-future handovers are not missed.
            var anchors = new List<DateTime> { now.AddDays(-1), shift.CreatedUtc };
            if (shift.ValidFrom.HasValue) anchors.Add(TenantTimeZone.StartOfLocalDay(shift.ValidFrom.Value.Date, zone));
            foreach (var assignment in assignments.Where(a => a.ShiftId == id))
            {
                if (assignment.Periods.Count > 0)
                    anchors.AddRange(assignment.Periods.Select(p => p.FromUtc));
                else anchors.Add(assignment.AssignedAtUtc);
            }
            anchors.AddRange(exceptions.Where(e => e.ShiftId == id).Select(e => e.StartUtc));
            foreach (var item in overrides.Where(a => a.ShiftId == id))
            {
                if (item.OriginalStartUtc.HasValue) anchors.Add(item.OriginalStartUtc.Value);
                else if (item.ExtraExceptionId.HasValue && await shifts.GetOccurrenceExceptionAsync(item.ExtraExceptionId.Value, ct) is { } extra)
                    anchors.Add(extra.StartUtc);
            }
            var windows = anchors.Where(a => a >= now).Select(a => a.AddDays(-1)).ToList();
            // Past assigned visits can still be started. Scan finite history in bounded pages;
            // being in the past does not mean the visit was completed.
            var historyStart = anchors.Min().Date;
            if (shift.ValidFrom.HasValue)
                historyStart = new[] { historyStart, TenantTimeZone.StartOfLocalDay(shift.ValidFrom.Value.Date, zone) }.Max();
            for (var cursor = historyStart; cursor <= now; cursor = cursor.AddDays(63)) windows.Add(cursor);
            foreach (var anchor in windows.Distinct())
            {
                var roster = await shifts.ResolveRosterAsync(shift, anchor, anchor.AddDays(63), zone, ct);
                if (roster.Any(v => v.EmployeeIds.Contains(employeeId)
                    && (!shift.ValidFrom.HasValue || TenantTimeZone.LocalDate(v.Occurrence.StartUtc, zone) >= shift.ValidFrom.Value.Date)
                    && (!shift.ValidUntil.HasValue || TenantTimeZone.LocalDate(v.Occurrence.StartUtc, zone) <= shift.ValidUntil.Value.Date)
                    && !forms.Any(f => f.ShiftId == id && f.ShiftOccurrenceUtc == v.Occurrence.StartUtc && f.IsSubmitted))) Deny();
            }
        }
    }

    private static void Deny() => throw new EmployeeIdentityConflictException(
        "This person still has assigned work or a cleaning offer. Reassign or finish that work before changing the role to Partner.");
}
