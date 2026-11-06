using CleaningSuite.Application.Partners;
using CleaningSuite.Domain.Partners;
using CleaningSuite.Domain.Shifts;
using Marten;
using MediatR;

namespace CleaningSuite.Application.Shifts;

public sealed record StaleAssignmentRepairCommand : IRequest<StaleAssignmentRepairResult>;

public sealed record StaleAssignmentRepairResult(IReadOnlyList<Guid> ReleasedAssignmentIds);

/// <summary>
/// Releases the active assignments that <see cref="StaleAssignmentRule"/> finds: cleaners still attached to a
/// deactivated shift or to a cancelled partner request. Released assignments are no longer active, so running the
/// repair again releases nothing new.
/// </summary>
public sealed class StaleAssignmentRepairHandler(IShiftRepository shifts, IPartnerStore? partners = null)
    : IRequestHandler<StaleAssignmentRepairCommand, StaleAssignmentRepairResult>
{
    public async Task<StaleAssignmentRepairResult> Handle(StaleAssignmentRepairCommand request, CancellationToken ct)
    {
        var all = await shifts.ListIncludingInactiveAsync(ct);
        var shiftsById = all.ToDictionary(shift => shift.Id);
        var assignments = new List<ShiftAssignment>();
        foreach (var shift in all)
            assignments.AddRange(await shifts.ListAssignmentsByShiftAsync(shift.Id, ct));

        var stale = StaleAssignmentRule.Find(assignments, shiftsById, await PartnerStatusesAsync(all, ct));
        var now = DateTime.UtcNow;
        foreach (var assignment in stale)
        {
            assignment.Release(now);
            await shifts.SaveAssignmentAsync(assignment, ct);
        }
        return new StaleAssignmentRepairResult(stale.Select(assignment => assignment.Id).ToList());
    }

    // Partner status is only needed for shifts that came from a partner request. Without a store, only deactivated shifts are repaired.
    private async Task<IReadOnlyDictionary<Guid, string>> PartnerStatusesAsync(IReadOnlyList<Shift> all, CancellationToken ct)
    {
        var requestIds = all.Where(shift => shift.PartnerRequestId is not null)
            .Select(shift => shift.PartnerRequestId!.Value).Distinct().ToArray();
        if (partners is null || requestIds.Length == 0) return new Dictionary<Guid, string>();
        var requests = await partners.Session.Query<PartnerCleaningRequest>()
            .Where(request => requestIds.Contains(request.Id)).ToListAsync(ct);
        return requests.ToDictionary(request => request.Id, request => request.Status);
    }
}
