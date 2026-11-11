using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Invoicing;
using CleaningSuite.Domain.Partners;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;

namespace CleaningSuite.Application.Shifts;

public enum BranchShiftMergeAction { Merge, Remove, Keep }

public sealed record BranchShiftMergeFinding(Guid ShiftId, Guid BranchId, Guid? RequestId, BranchShiftMergeAction Action, string Reason,
    DateTime? VisitStartUtc = null);

/// <summary>
/// Plans how partner bookings that still own a whole shift are folded into their branch's main shift.
/// An accepted booking's shift becomes one visit on the main shift. A shift whose booking was cancelled or sent
/// back, and that has no work or invoice, is removed. Anything unusual is kept and reported for a person to review:
/// the plan never guesses with work records or invoiced visits.
/// </summary>
public static class BranchShiftMerge
{
    public static Guid VisitId(Guid requestId) => PartnerCleaningRequest.StableId($"partner-occurrence:{requestId}");

    public static IReadOnlyList<BranchShiftMergeFinding> Plan(IEnumerable<Shift> shifts, IEnumerable<PartnerCleaningRequest> requests,
        IEnumerable<ShiftOccurrenceException> exceptions, IEnumerable<ShiftOccurrenceAssignment> overrides,
        IEnumerable<QualityCycleForm> forms, IEnumerable<Invoice> invoices, IEnumerable<Branch> branches)
    {
        var all = shifts.ToList();
        var requestsById = requests.ToDictionary(x => x.Id);
        var exceptionsByShift = exceptions.ToLookup(x => x.ShiftId);
        var overridden = overrides.Select(x => x.ShiftId).ToHashSet();
        var worked = forms.Where(f => f.ShiftDeletedUtc is null && (f.StartedAtUtc.HasValue || f.IsSubmitted)).ToLookup(f => f.ShiftId);
        var billed = invoices.Where(i => i.Status is Invoice.StatusIssued or Invoice.StatusPaid)
            .SelectMany(i => i.Lines).Select(l => l.ShiftId).OfType<Guid>().ToHashSet();
        var branchesById = branches.ToDictionary(x => x.Id);
        var mains = all.Where(x => x.IsBranchMain).ToDictionary(x => x.BranchId);
        // Starts already taken on each branch's main shift; planned merges add to them, in visit order.
        var taken = mains.Values.ToDictionary(x => x.BranchId, x => exceptionsByShift[x.Id].Select(e => e.StartUtc).ToHashSet());
        var findings = new List<BranchShiftMergeFinding>();

        foreach (var shift in all.Where(x => x.PartnerRequestId.HasValue && !x.IsBranchMain)
            .OrderBy(x => x.BranchId).ThenBy(x => exceptionsByShift[x.Id].Select(e => e.StartUtc).DefaultIfEmpty(DateTime.MaxValue).Min()).ThenBy(x => x.Id))
        {
            var requestId = shift.PartnerRequestId!.Value;
            BranchShiftMergeFinding Keep(string reason) => new(shift.Id, shift.BranchId, requestId, BranchShiftMergeAction.Keep, reason);
            if (!requestsById.TryGetValue(requestId, out var request) || request.LinkedShiftId != shift.Id)
            { findings.Add(Keep("Its partner request is missing or linked elsewhere.")); continue; }
            if (overridden.Contains(shift.Id)) { findings.Add(Keep("It has hand-made roster changes.")); continue; }
            var visitId = VisitId(requestId);
            var own = exceptionsByShift[shift.Id].ToList();
            if (own.Any(x => x.Id != visitId)) { findings.Add(Keep("It has schedule changes besides the booking's own visit.")); continue; }
            var visit = own.SingleOrDefault();
            var work = worked[shift.Id].ToList();

            if (request.Status == PartnerCleaningRequest.StatusAccepted && shift.IsActive && visit is not null)
            {
                if (work.Any(f => f.ShiftOccurrenceUtc != visit.StartUtc)) { findings.Add(Keep("It has work records at another time than the booking's visit.")); continue; }
                if (!mains.ContainsKey(shift.BranchId) && !(branchesById.TryGetValue(shift.BranchId, out var branch) && branch.HasNameDetails))
                { findings.Add(Keep("The property needs its size and cleaning type before a main shift can be named.")); continue; }
                if (!taken.TryGetValue(shift.BranchId, out var starts)) taken[shift.BranchId] = starts = [];
                if (!starts.Add(visit.StartUtc)) { findings.Add(Keep("Another visit of the same property starts at exactly the same time.")); continue; }
                findings.Add(new(shift.Id, shift.BranchId, requestId, BranchShiftMergeAction.Merge, "Accepted booking becomes a visit on the property's main shift.", visit.StartUtc));
                continue;
            }
            if (request.Status == PartnerCleaningRequest.StatusAccepted) { findings.Add(Keep("An accepted booking without an active shift and visit needs a person to look at it.")); continue; }
            if (work.Count > 0) { findings.Add(Keep("Its booking is not accepted, yet it has work records.")); continue; }
            if (billed.Contains(shift.Id)) { findings.Add(Keep("Its booking is not accepted, yet it has been invoiced.")); continue; }
            findings.Add(new(shift.Id, shift.BranchId, requestId, BranchShiftMergeAction.Remove, "Left over from a cancelled or re-opened booking, with no work and no invoice."));
        }
        return findings;
    }
}
