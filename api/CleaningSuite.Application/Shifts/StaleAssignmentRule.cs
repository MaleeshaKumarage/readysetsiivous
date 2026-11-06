using CleaningSuite.Domain.Shifts;

namespace CleaningSuite.Application.Shifts;

/// <summary>
/// Decides which active assignments are stale: the shift is deactivated, or it was created for a partner
/// request that has been cancelled. Such assignments can block a cleaner with an overlap that has no visible visit.
/// A pure rule, so the repair can be reviewed and tested before anything is changed.
/// </summary>
public static class StaleAssignmentRule
{
    public static IReadOnlyList<ShiftAssignment> Find(
        IEnumerable<ShiftAssignment> assignments,
        IReadOnlyDictionary<Guid, Shift> shiftsById,
        IReadOnlyDictionary<Guid, string> partnerStatusByRequestId)
    {
        var stale = new List<ShiftAssignment>();
        foreach (var assignment in assignments)
        {
            if (!assignment.IsActive) continue;
            if (!shiftsById.TryGetValue(assignment.ShiftId, out var shift)) continue;
            var deactivated = !shift.IsActive;
            var cancelledPartner = shift.PartnerRequestId is Guid requestId
                && partnerStatusByRequestId.TryGetValue(requestId, out var status) && status == "Cancelled";
            if (deactivated || cancelledPartner) stale.Add(assignment);
        }
        return stale;
    }
}
