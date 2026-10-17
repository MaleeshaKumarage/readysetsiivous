using System.Security.Cryptography;
using System.Text;
using CleaningSuite.Domain.Shifts;

namespace CleaningSuite.Application.Shifts;

public static class ShiftRosterReader
{
    public static ShiftOccurrenceDto ToDto(ShiftVisitRosterEntry entry) => new(entry.Occurrence.StartUtc, entry.Occurrence.EndUtc,
        entry.Occurrence.ExceptionId, entry.Occurrence.OriginalStartUtc, entry.Occurrence.Status, entry.ScheduleReason, entry.Occurrence.EmployeeId)
    {
        AssigneeIds = entry.EmployeeIds, AssignmentSource = entry.AssignmentSource,
        AssignmentMode = entry.Assignment?.Mode, AssignmentVersion = entry.Assignment?.Version,
        OverrideEmployeeIds = entry.Assignment?.EmployeeIds.ToArray() ?? Array.Empty<Guid>(),
        AssignmentReason = entry.Assignment?.Reason, RosterVersion = entry.RosterVersion
    };

    public static async Task<IReadOnlyList<ShiftVisitRosterEntry>> ResolveRosterAsync(this IShiftRepository shifts,
        Shift shift, DateTime from, DateTime to, TimeZoneInfo zone, CancellationToken ct,
        bool includeCancelled = false, IEnumerable<ShiftAssignment>? fallbackAssignments = null)
    {
        var (localFrom, localTo) = TenantTimeZone.LocalDatesForUtcRange(from, to, zone);
        var exceptions = await shifts.ListOccurrenceExceptionsAsync(shift.Id, from, to, ct) ?? Array.Empty<ShiftOccurrenceException>();
        var history = await shifts.ListAssignmentHistoryAsync(shift.Id, ct) ?? Array.Empty<ShiftAssignment>();
        var assignments = history.Concat(fallbackAssignments ?? Array.Empty<ShiftAssignment>())
            .GroupBy(x => (x.Id, x.EmployeeId)).Select(g => g.First());
        var overrides = await shifts.ListOccurrenceAssignmentsAsync(shift.Id, ct) ?? Array.Empty<ShiftOccurrenceAssignment>();
        return ShiftVisitRoster.Resolve(shift.Id, ShiftScheduleCalculator.GenerateOccurrences(shift, localFrom, localTo, zone),
                exceptions, assignments, overrides, includeCancelled)
            .Where(x => x.Occurrence.StartUtc >= from && x.Occurrence.StartUtc < to).ToList();
    }

    public static string Version(IEnumerable<ShiftAssignment> assignments) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(string.Join("|", assignments.OrderBy(x => x.Id).Select(x =>
            $"{x.Id:N}:{x.Version:N}:{x.EmployeeId:N}:{x.AssignedAtUtc.Ticks}:{x.IsActive}:{x.UnassignedAtUtc?.Ticks}:" +
            string.Join(",", x.Periods.Select(p => $"{p.FromUtc.Ticks}-{p.ToUtc?.Ticks}")))))));
}
