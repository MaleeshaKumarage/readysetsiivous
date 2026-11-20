using CleaningSuite.Domain.Shifts;

namespace CleaningSuite.Application.Shifts;

public sealed record StaleAssignmentItem(Guid AssignmentId, Guid ShiftId, string ShiftName, Guid EmployeeId, string Reason);

/// <summary>
/// Read-only report of the active assignments that <see cref="StaleAssignmentRule"/> would release.
/// It writes nothing: the repair is run only after an admin has reviewed this list.
/// </summary>
public static class StaleAssignmentDryRun
{
    public static async Task<IReadOnlyList<StaleAssignmentItem>> Run(IShiftRepository shifts,
        Func<IReadOnlyCollection<Guid>, Task<IReadOnlyDictionary<Guid, string>>> partnerStatuses, CancellationToken ct = default)
    {
        var all = await shifts.ListIncludingInactiveAsync(ct);
        var byId = all.ToDictionary(s => s.Id);
        var assignments = new List<ShiftAssignment>();
        foreach (var shift in all)
            assignments.AddRange(await shifts.ListAssignmentsByShiftAsync(shift.Id, ct));
        var requestIds = all.Where(s => s.PartnerRequestId is not null).Select(s => s.PartnerRequestId!.Value).Distinct().ToArray();
        var statuses = requestIds.Length == 0 ? new Dictionary<Guid, string>() : await partnerStatuses(requestIds);
        var stale = StaleAssignmentRule.Find(assignments, byId, statuses);
        return stale.Select(a =>
        {
            var shift = byId[a.ShiftId];
            var reason = !shift.IsActive ? "Shift is deactivated" : "Partner request is cancelled";
            return new StaleAssignmentItem(a.Id, a.ShiftId, shift.Name, a.EmployeeId, reason);
        }).ToArray();
    }
}
