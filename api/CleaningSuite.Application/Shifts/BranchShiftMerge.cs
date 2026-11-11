using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Invoicing;
using CleaningSuite.Domain.Partners;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;

namespace CleaningSuite.Application.Shifts;

public enum BranchShiftMergeAction { Merge, Remove, Keep }

public sealed record BranchShiftMergeFinding(Guid ShiftId, Guid BranchId, Guid? RequestId, BranchShiftMergeAction Action, string Reason,
    DateTime? VisitStartUtc = null)
{
    /// <summary>"PartnerBooking" or "OneTime".</summary>
    public string Kind { get; init; } = BranchShiftMerge.KindPartnerBooking;
    public DateTime? VisitEndUtc { get; init; }
    /// <summary>The cleaners who hold a one-time shift's visit today.</summary>
    public IReadOnlyList<Guid> EmployeeIds { get; init; } = [];
    /// <summary>The one-time shift's own price, kept on the visit when it differs from the property price on that date.</summary>
    public ShiftOccurrencePrice? KeptPrice { get; init; }
}

/// <summary>
/// Plans how shifts that hold a single visit are folded into their branch's main shift: partner bookings that still
/// own a shift, and one-time shifts staff created. An active one becomes one visit on the main shift. A shift left
/// over from a cancelled or re-opened booking, or a deactivated one-time shift, with no work and no invoice, is
/// removed. Anything unusual is kept and reported for a person to review: the plan never guesses with work records,
/// cleaners or prices.
/// </summary>
public static class BranchShiftMerge
{
    public const string KindPartnerBooking = "PartnerBooking";
    public const string KindOneTime = "OneTime";
    public const string KeptPriceReason = "Price kept from the one-time shift";

    public static Guid VisitId(Guid requestId) => PartnerCleaningRequest.StableId($"partner-occurrence:{requestId}");
    public static Guid OneTimeVisitId(Guid shiftId) => PartnerCleaningRequest.StableId($"one-time-visit:{shiftId}");

    public static IReadOnlyList<BranchShiftMergeFinding> Plan(IEnumerable<Shift> shifts, IEnumerable<PartnerCleaningRequest> requests,
        IEnumerable<ShiftOccurrenceException> exceptions, IEnumerable<ShiftOccurrenceAssignment> overrides,
        IEnumerable<QualityCycleForm> forms, IEnumerable<Invoice> invoices, IEnumerable<Branch> branches,
        IEnumerable<ShiftAssignment>? assignments = null, IEnumerable<ShiftBillingProfile>? billing = null, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Utc;
        var all = shifts.ToList();
        var requestsById = requests.ToDictionary(x => x.Id);
        var exceptionsByShift = exceptions.ToLookup(x => x.ShiftId);
        var overridden = overrides.Select(x => x.ShiftId).ToHashSet();
        var worked = forms.Where(f => f.ShiftDeletedUtc is null && (f.StartedAtUtc.HasValue || f.IsSubmitted)).ToLookup(f => f.ShiftId);
        var billed = invoices.Where(i => i.Status is Invoice.StatusIssued or Invoice.StatusPaid)
            .SelectMany(i => i.Lines).Select(l => l.ShiftId).OfType<Guid>().ToHashSet();
        var branchesById = branches.ToDictionary(x => x.Id);
        var assignmentsByShift = (assignments ?? []).ToLookup(x => x.ShiftId);
        var billingByShift = (billing ?? []).ToLookup(x => x.ShiftId);
        var mains = all.Where(x => x.IsBranchMain).ToDictionary(x => x.BranchId);
        // Starts already taken on each branch's main shift; planned merges add to them, in visit order.
        var taken = mains.Values.ToDictionary(x => x.BranchId, x => exceptionsByShift[x.Id].Select(e => e.StartUtc).ToHashSet());
        bool CanName(Guid branchId) => mains.ContainsKey(branchId) || (branchesById.TryGetValue(branchId, out var branch) && branch.HasNameDetails);
        // A deactivated main shift takes no visits until an admin reactivates it; the merge never revives it.
        bool MainInactive(Guid branchId) => mains.TryGetValue(branchId, out var main) && !main.IsActive;
        bool Claim(Guid branchId, DateTime start)
        {
            if (!taken.TryGetValue(branchId, out var starts)) taken[branchId] = starts = [];
            return starts.Add(start);
        }
        ShiftOccurrence? OneTimeVisit(Shift shift) => shift.Schedule.OneTimeDate is { } date
            ? ShiftScheduleCalculator.GenerateOccurrences(shift, date.Date, date.Date, zone).SingleOrDefault() : null;
        DateTime VisitStart(Shift shift) => shift.Schedule.IsOneTime ? OneTimeVisit(shift)?.StartUtc ?? DateTime.MaxValue
            : exceptionsByShift[shift.Id].Select(e => e.StartUtc).DefaultIfEmpty(DateTime.MaxValue).Min();

        var findings = new List<BranchShiftMergeFinding>();
        foreach (var shift in all.Where(x => !x.IsBranchMain && (x.PartnerRequestId.HasValue || x.Schedule.IsOneTime))
            .OrderBy(x => x.BranchId).ThenBy(VisitStart).ThenBy(x => x.Id))
        {
            if (!shift.PartnerRequestId.HasValue) { findings.Add(OneTime(shift)); continue; }
            var requestId = shift.PartnerRequestId.Value;
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
                // An invoice names this shift; moving it would leave the invoice pointing at a shift that no longer exists.
                if (billed.Contains(shift.Id)) { findings.Add(Keep("Its booking is invoiced, so its shift stays linked to the invoice.")); continue; }
                if (MainInactive(shift.BranchId)) { findings.Add(Keep("The property's visit shift is deactivated.")); continue; }
                if (work.Any(f => f.ShiftOccurrenceUtc != visit.StartUtc)) { findings.Add(Keep("It has work records at another time than the booking's visit.")); continue; }
                if (!CanName(shift.BranchId)) { findings.Add(Keep("The property needs its size and cleaning type before a main shift can be named.")); continue; }
                if (!Claim(shift.BranchId, visit.StartUtc)) { findings.Add(Keep("Another visit of the same property starts at exactly the same time.")); continue; }
                findings.Add(new(shift.Id, shift.BranchId, requestId, BranchShiftMergeAction.Merge, "Accepted booking becomes a visit on the property's main shift.", visit.StartUtc));
                continue;
            }
            if (request.Status == PartnerCleaningRequest.StatusAccepted) { findings.Add(Keep("An accepted booking without an active shift and visit needs a person to look at it.")); continue; }
            if (work.Count > 0) { findings.Add(Keep("Its booking is not accepted, yet it has work records.")); continue; }
            if (billed.Contains(shift.Id)) { findings.Add(Keep("Its booking is not accepted, yet it has been invoiced.")); continue; }
            findings.Add(new(shift.Id, shift.BranchId, requestId, BranchShiftMergeAction.Remove, "Left over from a cancelled or re-opened booking, with no work and no invoice."));
        }
        return findings;

        BranchShiftMergeFinding OneTime(Shift shift)
        {
            BranchShiftMergeFinding Keep(string reason) => new(shift.Id, shift.BranchId, null, BranchShiftMergeAction.Keep, reason) { Kind = KindOneTime };
            var work = worked[shift.Id].ToList();
            if (overridden.Contains(shift.Id)) return Keep("It has hand-made roster changes for its visit.");
            if (exceptionsByShift[shift.Id].Any()) return Keep("Its visit was moved, cancelled or given an extra visit.");
            if (shift.QualityCyclePlans.Any(p => p.BranchId != shift.BranchId || p.CompanyId != shift.CompanyId))
                return Keep("It belonged to another property or company earlier.");
            if (!shift.IsActive)
            {
                if (work.Count > 0) return Keep("It is deactivated, yet it has work records.");
                if (billed.Contains(shift.Id)) return Keep("It is deactivated, yet it has been invoiced.");
                return new(shift.Id, shift.BranchId, null, BranchShiftMergeAction.Remove, "Deactivated one-time shift with no work and no invoice.") { Kind = KindOneTime };
            }
            // An invoice names this shift; moving it would leave the invoice pointing at a shift that no longer exists.
            if (billed.Contains(shift.Id)) return Keep("It has been invoiced, so its shift stays linked to the invoice.");
            if (MainInactive(shift.BranchId)) return Keep("The property's visit shift is deactivated.");
            var visit = OneTimeVisit(shift);
            if (visit is null) return Keep("Its one-time date and times do not produce a visit.");
            if (work.Any(f => f.ShiftOccurrenceUtc != visit.StartUtc)) return Keep("It has work records at another time than its visit.");
            if (!CanName(shift.BranchId)) return Keep("The property needs its size and cleaning type before a main shift can be named.");
            // A visit still to be done would switch to the main shift's checklist; a different one is a decision for a person.
            var done = work.Any(f => f.IsSubmitted);
            if (!done && mains.TryGetValue(shift.BranchId, out var main) && main.QualityCycleTemplateId is Guid mainTemplate
                && shift.QualityCycleTemplateId != mainTemplate)
                return Keep("Its checklist differs from the property's main shift, and its visit is not finished yet.");

            // The price the shift is billed at today, compared with what the main shift would charge on that date.
            var date = DateOnly.FromDateTime(shift.Schedule.OneTimeDate!.Value.Date);
            var profile = billingByShift[shift.Id].FirstOrDefault();
            var branchOf = branchesById.TryGetValue(shift.BranchId, out var branch) ? branch : null;
            var versions = branchOf?.Versions ?? [];
            var inherited = versions.LastOrDefault(v => v.EffectiveFrom <= date) ?? versions.FirstOrDefault();
            (string Mode, decimal Rate, decimal Vat)? own = profile?.BranchPriceEffectiveFrom is { } from
                ? branchOf?.VersionFrom(from) is { } found ? ((string, decimal, decimal)?)(found.Mode, found.PriceNet, found.VatRatePercent) : null
                : profile?.Plans.Where(p => p.EffectiveFrom <= date).OrderByDescending(p => p.EffectiveFrom)
                    .Select(p => ((string, decimal, decimal)?)(p.Mode, p.RateNet, p.VatRatePercent)).FirstOrDefault();
            var special = profile?.Overrides.FirstOrDefault(o => o.OccurrenceStartUtc == visit.StartUtc);
            ShiftOccurrencePrice? kept = null;
            if (own is { } price)
            {
                var rate = price.Mode == "PerOccurrence" ? special?.RateNet ?? price.Rate : price.Rate;
                var same = inherited is not null && inherited.Mode == price.Mode && inherited.PriceNet == rate && inherited.VatRatePercent == price.Vat;
                if (!same)
                {
                    if (price.Mode != "PerOccurrence") return Keep("It is priced hourly or monthly, differently from the property.");
                    if (inherited is not { Mode: "PerOccurrence" }) return Keep("It has its own per-visit price, but the property is not priced per visit.");
                    kept = new(visit.StartUtc, rate, special?.Reason ?? KeptPriceReason, price.Vat);
                }
            }
            else if (inherited is null && !billed.Contains(shift.Id) && done) return Keep("Neither it nor its property has a price, and its visit is finished and not invoiced.");

            var legacyStart = shift.ValidFrom.HasValue ? TenantTimeZone.StartOfLocalDay(shift.ValidFrom.Value.Date, zone) : (DateTime?)null;
            var cleaners = ShiftVisitRoster.Resolve(shift.Id, [visit], [], assignmentsByShift[shift.Id], [], legacyStartUtc: legacyStart).Single().EmployeeIds;
            if (!Claim(shift.BranchId, visit.StartUtc)) return Keep("Another visit of the same property starts at exactly the same time.");
            return new(shift.Id, shift.BranchId, null, BranchShiftMergeAction.Merge, "One-time shift becomes a visit on the property's main shift.", visit.StartUtc)
                { Kind = KindOneTime, VisitEndUtc = visit.EndUtc, EmployeeIds = cleaners, KeptPrice = kept };
        }
    }
}
