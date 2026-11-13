using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Shifts;

namespace CleaningSuite.Application.Dashboard;

public sealed record DashboardRosterVisit(Guid CompanyId, IReadOnlyList<Guid> EmployeeIds, DateTime StartUtc, DateTime EndUtc, bool Cancelled);

/// <summary>The one roster walk the dashboard handlers share: every visit of every shift in a window.</summary>
public static class DashboardRoster
{
    /// <summary>
    /// Loads each shift's records for the window in one bulk query, then resolves the rosters in memory.
    /// Shifts keep their history: deactivated shifts are included.
    /// </summary>
    public static async Task<(List<DashboardRosterVisit> Visits, Dictionary<Guid, Guid> ShiftCompany)> LoadAsync(
        IShiftRepository shifts, DateTime fromUtc, DateTime toUtc, TimeZoneInfo zone, CancellationToken ct)
    {
        var all = await shifts.ListAllAsync(ct);
        var inputs = await shifts.ListRosterInputsAsync(all.Select(s => s.Id).ToArray(), fromUtc, toUtc, ct);
        var visits = new List<DashboardRosterVisit>();
        var shiftCompany = new Dictionary<Guid, Guid>();
        foreach (var shift in all)
        {
            shiftCompany[shift.Id] = shift.CompanyId;
            var exceptions = inputs.Exceptions.TryGetValue(shift.Id, out var e) ? e : Array.Empty<ShiftOccurrenceException>();
            var history = inputs.History.TryGetValue(shift.Id, out var h) ? h : Array.Empty<ShiftAssignment>();
            var overrides = inputs.Overrides.TryGetValue(shift.Id, out var o) ? o : Array.Empty<ShiftOccurrenceAssignment>();
            foreach (var entry in ShiftRosterReader.ResolveRoster(shift, fromUtc, toUtc, zone, exceptions, history, overrides))
                visits.Add(new(shift.CompanyId, entry.EmployeeIds, entry.Occurrence.StartUtc, entry.Occurrence.EndUtc, entry.IsCancelled));
        }
        return (visits, shiftCompany);
    }
}
