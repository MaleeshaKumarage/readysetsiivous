using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Shifts;
using Marten;

namespace CleaningSuite.Application.Shifts;

/// <summary>
/// One on-call shift per branch collects that branch's visits. A partner booking or an admin-added visit is an
/// extra occurrence on it, so a branch no longer gathers one shift per visit.
/// </summary>
public static class BranchMainShifts
{
    /// <summary>
    /// Loads the branch's main shift, or creates it. The id is derived from the branch, and callers hold the tenant
    /// work reservation, so a branch can never get two. The shift is stored by the caller, in the caller's commit.
    /// </summary>
    public static async Task<Shift> FindOrCreateAsync(IDocumentSession session, Branch branch, DateTime visitLocalDate, CancellationToken ct)
    {
        var id = Shift.BranchMainId(branch.Id);
        var shift = await session.LoadAsync<Shift>(id, ct);
        if (shift is null)
        {
            ShiftNaming.EnsureDetails(branch);
            shift = Shift.Create(branch.CompanyId, branch.Id, ShiftNaming.Build(branch),
                new ShiftSchedule { Type = ShiftScheduleType.OnCallFlexible }, null, visitLocalDate.Date, null);
            shift.Id = id; shift.IsBranchMain = true;
        }
        // New work on a deactivated main shift makes it live again; its earlier visits were kept.
        shift.IsActive = true; shift.ValidUntil = null;
        shift.CoverVisitDate(visitLocalDate);
        return shift;
    }

    /// <summary>
    /// Work records and invoice lines find a visit by its shift and start time, so two visits on one main shift
    /// never share a start. A visit that would collide starts one second later for each visit already there.
    /// </summary>
    public static DateTime FreeStart(DateTime start, DateTime end, IEnumerable<ShiftOccurrenceException> others)
    {
        var taken = others.Select(x => x.StartUtc).ToHashSet();
        while (taken.Contains(start) && start.AddSeconds(1) < end) start = start.AddSeconds(1);
        return start;
    }
}
