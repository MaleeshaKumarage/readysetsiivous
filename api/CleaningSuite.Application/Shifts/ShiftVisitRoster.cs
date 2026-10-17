using CleaningSuite.Domain.Shifts;

namespace CleaningSuite.Application.Shifts;

public record ShiftVisitRosterEntry(ResolvedShiftOccurrence Occurrence, DateTime? OriginalStartUtc,
    Guid? ExtraExceptionId, IReadOnlyList<Guid> EmployeeIds, string AssignmentSource,
    ShiftOccurrenceAssignment? Assignment)
{
    public bool IsCancelled => Occurrence.Status is ShiftOccurrenceExceptionStatus.CustomerCancelled
        or ShiftOccurrenceExceptionStatus.CompanyCancelled;
}

/// <summary>The shared roster policy for lists, clock starts, dispatch and reporting.</summary>
public static class ShiftVisitRoster
{
    public static IReadOnlyList<ShiftVisitRosterEntry> Resolve(Guid shiftId, IEnumerable<ShiftOccurrence> generated,
        IEnumerable<ShiftOccurrenceException> exceptions, IEnumerable<ShiftAssignment> recurring,
        IEnumerable<ShiftOccurrenceAssignment> overrides, bool includeCancelled = false)
    {
        var periods = recurring.Where(x => x.ShiftId == shiftId).ToList();
        var changes = overrides.Where(x => x.ShiftId == shiftId).GroupBy(x => x.Id)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(item => item.UpdatedUtc).First());
        return ShiftOccurrenceResolver.Apply(generated, exceptions.Where(x => x.ShiftId == shiftId), includeCancelled)
            .Select(visit =>
            {
                var extra = visit.Status == ShiftOccurrenceExceptionStatus.Extra ? visit.ExceptionId : null;
                DateTime? original = extra.HasValue ? null : visit.OriginalStartUtc ?? visit.StartUtc;
                changes.TryGetValue(ShiftOccurrenceAssignment.Identity(shiftId, original, extra), out var change);
                var regular = periods.Where(x => x.CoversVisit(visit.StartUtc, visit.EndUtc)).Select(x => x.EmployeeId);
                IEnumerable<Guid> selected; string source;
                if (change is not null)
                {
                    selected = change.Mode switch {
                        OccurrenceAssignmentMode.Replace => change.EmployeeIds,
                        OccurrenceAssignmentMode.Add => regular.Concat(change.EmployeeIds),
                        _ => regular
                    };
                    source = change.Mode switch { OccurrenceAssignmentMode.Replace => "Replacement", OccurrenceAssignmentMode.Add => "Extra employees", _ => "Recurring" };
                }
                else
                {
                    selected = visit.EmployeeId.HasValue ? new[] { visit.EmployeeId.Value } : regular;
                    source = visit.EmployeeId.HasValue ? "Replacement" : "Recurring";
                }
                var cancelled = visit.Status is ShiftOccurrenceExceptionStatus.CustomerCancelled or ShiftOccurrenceExceptionStatus.CompanyCancelled;
                return new ShiftVisitRosterEntry(visit, original, extra,
                    cancelled ? Array.Empty<Guid>() : selected.Where(id => id != Guid.Empty).Distinct().Order().ToArray(),
                    cancelled ? "Cancelled" : source, change);
            }).ToList();
    }
}
