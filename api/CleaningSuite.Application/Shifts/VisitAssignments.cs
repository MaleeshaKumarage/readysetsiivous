using CleaningSuite.Application.Common;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Shifts;
using FluentValidation;
using MediatR;

namespace CleaningSuite.Application.Shifts;

public record SetVisitAssigneesCommand(Guid ShiftId, DateTime? OriginalStartUtc, Guid? ExtraExceptionId,
    OccurrenceAssignmentMode Mode, IReadOnlyList<Guid> EmployeeIds, string Reason,
    string RosterVersion, Guid? AssignmentVersion = null, bool FromThisDate = false,
    bool Preview = false) : IRequest<VisitAssignmentResult>;
public record VisitAssignmentResult(IReadOnlyList<Guid> EmployeeIds, string Message, bool Preview);

public sealed class SetVisitAssigneesHandler(IShiftRepository shifts, IEmployeeRepository employees,
    IQualityCycleRepository forms, ITenantProfileRepository? profiles = null,
    ITenantContext? tenant = null, IShiftWorkReservation? reservation = null, TimeProvider? clock = null)
    : IRequestHandler<SetVisitAssigneesCommand, VisitAssignmentResult>
{
    public async Task<VisitAssignmentResult> Handle(SetVisitAssigneesCommand request, CancellationToken ct)
    {
        await using var lease = reservation is null || request.Preview ? null : await reservation.AcquireAsync(ct);
        if (request.EmployeeIds is null || !Enum.IsDefined(request.Mode)
            || request.EmployeeIds.Any(x => x == Guid.Empty) || request.EmployeeIds.Count > 100
            || (request.Mode != OccurrenceAssignmentMode.Inherit && request.EmployeeIds.Count == 0)
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500)
            throw new ValidationException("Select employees, a supported action and a reason of at most 500 characters.");
        if (request.FromThisDate && request.Mode == OccurrenceAssignmentMode.Inherit)
            throw new ValidationException("Restore assignment is available for one visit only.");
        if (request.OriginalStartUtc.HasValue == request.ExtraExceptionId.HasValue)
            throw new ValidationException("Choose one scheduled occurrence or extra visit.");
        var shift = await shifts.GetAsync(request.ShiftId, ct) ?? throw new NotFoundException("Shift", request.ShiftId);
        if (!shift.IsActive) throw new ShiftConflictException("Activate the shift before assigning visits.");
        var (_, zone) = await TenantTimeZone.LoadAsync(profiles, tenant, ct);
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var anchor = request.OriginalStartUtc.HasValue ? Utc(request.OriginalStartUtc.Value)
            : (await shifts.GetOccurrenceExceptionAsync(request.ExtraExceptionId!.Value, ct) is { } extra && extra.ShiftId == shift.Id
                ? extra.StartUtc : throw new ShiftConflictException("The extra visit no longer exists."));
        var originalDay = TenantTimeZone.LocalDate(anchor, zone);
        var from = TenantTimeZone.StartOfLocalDay(originalDay, zone);
        var to = TenantTimeZone.StartOfLocalDay(originalDay.AddDays(1), zone);
        var roster = await shifts.ResolveRosterAsync(shift, from, to, zone, ct, includeCancelled: true);
        // A moved visit can be outside the original day. Locate it using its stable key.
        var exceptions = await shifts.ListOccurrenceExceptionsAsync(shift.Id, from, to, ct) ?? [];
        var moved = exceptions.FirstOrDefault(x => x.OriginalStartUtc == anchor);
        if (moved is not null && (moved.StartUtc < from || moved.StartUtc >= to))
            roster = await shifts.ResolveRosterAsync(shift, moved.StartUtc, moved.StartUtc.AddTicks(1), zone, ct, includeCancelled: true);
        var visit = roster.SingleOrDefault(x => request.ExtraExceptionId.HasValue
            ? x.ExtraExceptionId == request.ExtraExceptionId : x.OriginalStartUtc == anchor)
            ?? throw new ShiftConflictException("This occurrence no longer exists. Reload the list.");
        if (visit.IsCancelled) throw new ShiftConflictException("Restore the cancelled visit before assigning employees.");
        var day = TenantTimeZone.LocalDate(visit.Occurrence.StartUtc, zone);
        if (day < TenantTimeZone.LocalDate(now, zone)) throw new ShiftConflictException("Past visits cannot be reassigned.");
        if (request.RosterVersion != visit.RosterVersion || request.AssignmentVersion != visit.Assignment?.Version)
            throw new ShiftConflictException("The roster changed. Reload occurrences and try again.");
        var selected = request.EmployeeIds.Distinct().Order().ToArray();
        foreach (var id in selected)
        {
            var employee = await employees.GetByIdAsync(id, ct) ?? throw new NotFoundException("Employee", id);
            if (!employee.IsActive) throw new ShiftConflictException("Choose active employees only.");
        }
        var boundary = TenantTimeZone.StartOfLocalDay(day, zone);
        var saved = await forms.ListFormsAsync(shift.Id, request.FromThisDate ? boundary : null, null, ct) ?? [];
        if (saved.Any(f => (f.IsSubmitted || f.StartedAtUtc.HasValue) && (request.FromThisDate
                ? f.ShiftOccurrenceUtc >= boundary
                : f.ShiftOccurrenceUtc == anchor || f.ShiftOccurrenceUtc == visit.Occurrence.StartUtc)))
            throw new ShiftConflictException("This assignment affects started or completed work and cannot be changed.");
        var history = (await shifts.ListAssignmentHistoryAsync(shift.Id, ct) ?? []).ToList();
        ShiftOccurrenceAssignment? change = null;
        IReadOnlyList<ShiftVisitRosterEntry> candidate;
        var end = visit.Occurrence.EndUtc;
        if (request.FromThisDate)
        {
            // Match the established bounded recurring-assignment conflict window.
            end = TenantTimeZone.StartOfLocalDay(day.AddDays(180), zone);
            if (shift.ValidUntil.HasValue)
            {
                var until = TenantTimeZone.StartOfLocalDay(shift.ValidUntil.Value.Date.AddDays(1), zone);
                if (until < end) end = until;
            }
            if (end <= boundary) throw new ShiftConflictException("The handover date is outside this shift’s date range.");
            foreach (var item in history.Where(x => x.IsActive && x.Periods.Count == 0 && shift.ValidFrom.HasValue))
                item.Periods.Add(new() { FromUtc = TenantTimeZone.StartOfLocalDay(shift.ValidFrom!.Value.Date, zone) });
            if (request.Mode == OccurrenceAssignmentMode.Replace)
                foreach (var item in history) item.EndFrom(boundary, now);
            foreach (var id in selected)
            {
                var item = history.FirstOrDefault(x => x.EmployeeId == id);
                if (item is null) { item = new ShiftAssignment { ShiftId = shift.Id, EmployeeId = id, AssignedAtUtc = boundary }; history.Add(item); }
                item.BeginFrom(boundary, now, request.Reason.Trim());
            }
            var (localFrom, localTo) = TenantTimeZone.LocalDatesForUtcRange(boundary, end, zone);
            var changes = await shifts.ListOccurrenceExceptionsAsync(shift.Id, boundary, end, ct) ?? [];
            var overrides = await shifts.ListOccurrenceAssignmentsAsync(shift.Id, ct) ?? [];
            candidate = ShiftVisitRoster.Resolve(shift.Id, ShiftScheduleCalculator.GenerateOccurrences(shift, localFrom, localTo, zone), changes, history, overrides, legacyStartUtc: shift.ValidFrom.HasValue ? TenantTimeZone.StartOfLocalDay(shift.ValidFrom.Value.Date, zone) : null)
                .Where(x => x.Occurrence.StartUtc >= boundary && x.Occurrence.StartUtc < end).ToList();
        }
        else
        {
            change = visit.Assignment ?? ShiftOccurrenceAssignment.Create(shift.Id, visit.OriginalStartUtc,
                visit.ExtraExceptionId, request.Mode, selected, request.Reason);
            change.Update(request.Mode, selected, request.Reason);
            var regular = history.Where(x => x.CoversVisit(visit.Occurrence.StartUtc, visit.Occurrence.EndUtc, shift.ValidFrom.HasValue ? TenantTimeZone.StartOfLocalDay(shift.ValidFrom.Value.Date, zone) : null)).Select(x => x.EmployeeId);
            var ids = request.Mode switch { OccurrenceAssignmentMode.Replace => selected,
                OccurrenceAssignmentMode.Add => regular.Concat(selected).Distinct().Order().ToArray(),
                _ => regular.Distinct().Order().ToArray() };
            candidate = [visit with { EmployeeIds = ids, Assignment = change }];
        }
        await VisitAssignmentSafety.CheckConflicts(shifts, shift, candidate, zone, ct);
        var effective = candidate.FirstOrDefault(x => x.Occurrence.StartUtc == visit.Occurrence.StartUtc)?.EmployeeIds ?? selected;
        var message = request.FromThisDate
            ? "Recurring assignments updated from this tenant-local date. Later recurring handovers are replaced when using Replace. One-off assignments are preserved. Overlaps are checked through " + TenantTimeZone.LocalDate(end.AddTicks(-1), zone).ToString("yyyy-MM-dd") + "."
            : "Assignment updated for this visit only. Other visits are unchanged.";
        if (!request.Preview)
        {
            if (request.FromThisDate) await shifts.SaveAssignmentPeriodsAsync(history, ct);
            else await shifts.SaveOccurrenceAssignmentAsync(change!, ct);
        }
        return new(effective, message, request.Preview);
    }
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}

public static class VisitAssignmentSafety
{
    public static async Task CheckConflicts(IShiftRepository shifts, Shift current,
        IReadOnlyList<ShiftVisitRosterEntry> candidate, TimeZoneInfo zone, CancellationToken ct)
    {
        if (candidate.Count == 0) return;
        var from = candidate.Min(x => x.Occurrence.StartUtc).AddDays(-1);
        var to = candidate.Max(x => x.Occurrence.EndUtc).AddDays(1);
        // Discover only shifts involving the affected employees, including future periods and override-only work.
        var affected = candidate.SelectMany(x => x.EmployeeIds).Distinct().ToArray();
        var ids = new HashSet<Guid>();
        foreach (var employee in affected)
        {
            var history = await shifts.ListAssignmentHistoryByEmployeeAsync(employee, ct) ?? [];
            var active = await shifts.ListAssignmentsByEmployeeAsync(employee, ct) ?? [];
            var overrides = await shifts.ListOccurrenceAssignmentsByEmployeeAsync(employee, ct) ?? [];
            var legacy = await shifts.ListOccurrenceExceptionsByEmployeeAsync(employee, from, to, ct) ?? [];
            ids.UnionWith(history.Concat(active).Select(x => x.ShiftId));
            ids.UnionWith(overrides.Select(x => x.ShiftId)); ids.UnionWith(legacy.Select(x => x.ShiftId));
        }
        ids.Remove(current.Id);
        var others = ids.Count == 0 ? [] : await shifts.ListByIdsAsync(ids, ct) ?? [];
        var visits = candidate.ToList();
        var own = await shifts.ResolveRosterAsync(current, from, to, zone, ct);
        var changedKeys = candidate.Select(x => (x.OriginalStartUtc, x.ExtraExceptionId)).ToHashSet();
        visits.AddRange(own.Where(x => !changedKeys.Contains((x.OriginalStartUtc, x.ExtraExceptionId))));
        foreach (var other in others.Where(x => x.IsActive && x.Id != current.Id))
            visits.AddRange(await shifts.ResolveRosterAsync(other, from, to, zone, ct));
        var changed = new HashSet<ShiftVisitRosterEntry>(candidate, ReferenceEqualityComparer.Instance);
        foreach (var employee in affected)
        {
            var allEnd = DateTime.MinValue; var changedEnd = DateTime.MinValue;
            foreach (var visit in visits.Where(x => x.EmployeeIds.Contains(employee)).OrderBy(x => x.Occurrence.StartUtc))
            {
                var isChanged = changed.Contains(visit);
                if (visit.Occurrence.StartUtc < (isChanged ? allEnd : changedEnd))
                    throw new ShiftConflictException("An employee is already assigned to an overlapping visit. No assignments were saved.");
                if (visit.Occurrence.EndUtc > allEnd) allEnd = visit.Occurrence.EndUtc;
                if (isChanged && visit.Occurrence.EndUtc > changedEnd) changedEnd = visit.Occurrence.EndUtc;
            }
        }
    }
}
