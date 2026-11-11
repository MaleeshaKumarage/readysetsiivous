using CleaningSuite.Domain.Shifts;

namespace CleaningSuite.Application.Shifts;

/// <summary>Reads one visit of a branch main shift. Other shifts have no per-visit details, so they return null.</summary>
public static class BranchMainVisits
{
    public static async Task<ShiftOccurrenceException?> FindAsync(IShiftRepository shifts, Shift shift, DateTime startUtc, CancellationToken ct)
    {
        if (!shift.IsBranchMain) return null;
        var start = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
        // PostgreSQL keeps microseconds: a one-tick window can collapse to nothing, so the window is one second wide.
        var found = await shifts.ListOccurrenceExceptionsAsync(shift.Id, start, start.AddSeconds(1), ct) ?? [];
        return found.FirstOrDefault(x => x.Status == ShiftOccurrenceExceptionStatus.Extra && x.StartUtc == start);
    }
}
