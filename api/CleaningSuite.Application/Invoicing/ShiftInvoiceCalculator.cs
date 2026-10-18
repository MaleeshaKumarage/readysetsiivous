using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Invoicing;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using static CleaningSuite.Application.Invoicing.ShiftBillingSettingsService;

namespace CleaningSuite.Application.Invoicing;

public record MonthlyInvoiceOverride(Guid ShiftId, string Month, decimal AmountNet, string Reason);
public record BillableShiftVisit(DateTime StartUtc, DateTime EndUtc, Guid BranchId, string ShiftName, bool Completed);

/// <summary>Pure decimal calculation; consumes schedule/completion facts, never changes work evidence.</summary>
public static class ShiftInvoiceCalculator
{
    public static List<InvoiceLine> Calculate(Shift shift, ShiftBillingProfile profile, IReadOnlyList<BillableShiftVisit> visits,
        DateOnly from, DateOnly to, TimeZoneInfo zone, IReadOnlyDictionary<Guid, string> branches,
        IReadOnlyList<MonthlyInvoiceOverride> overrides)
    {
        if (profile.Plans.GroupBy(p => p.EffectiveFrom).Any(g => g.Count() > 1)) Invalid("Pricing has duplicate effective dates. Correct the pricing history before invoicing.");
        foreach (var plan in profile.Plans)
        {
            Rate(plan.RateNet, plan.VatRatePercent);
            if (plan.Mode is not ("Monthly" or "Hourly" or "PerOccurrence")) Invalid("The shift has an unsupported pricing mode. Correct pricing before invoicing.");
        }
        ShiftPricingPlan? Price(DateOnly date) => profile.Plans.Where(p => p.EffectiveFrom <= date).OrderByDescending(p => p.EffectiveFrom).FirstOrDefault();
        var history = shift.QualityCyclePlans.OrderBy(p => p.EffectiveFromUtc).ToArray();
        QualityCyclePlan? Active(DateOnly date)
        {
            var end = TenantTimeZone.StartOfLocalDay(date.AddDays(1).ToDateTime(TimeOnly.MinValue), zone);
            var plan = history.LastOrDefault(p => p.EffectiveFromUtc < end)
                ?? (history.Length == 0 ? new QualityCyclePlan { CompanyId = shift.CompanyId, BranchId = shift.BranchId,
                    ShiftName = shift.Name, IsActive = shift.IsActive, ValidFrom = shift.ValidFrom ?? TenantTimeZone.LocalDate(shift.CreatedUtc, zone), ValidUntil = shift.ValidUntil } : null);
            return plan is not null && plan.IsActive && plan.CompanyId == shift.CompanyId
                && (!plan.ValidFrom.HasValue || date >= DateOnly.FromDateTime(plan.ValidFrom.Value))
                && (!plan.ValidUntil.HasValue || date <= DateOnly.FromDateTime(plan.ValidUntil.Value)) ? plan : null;
        }
        string Branch(Guid id) => branches.TryGetValue(id, out var name) ? name : throw new ShiftConflictException("A shift branch is missing. Restore its company/branch association before invoicing.");
        var monthlyDays = new List<(DateOnly Date, ShiftPricingPlan Price, QualityCyclePlan Plan)>();
        var mixedBasisMonths = new HashSet<string>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var active = Active(day); if (active is null) continue;
            var price = Price(day);
            if (price is null) Invalid($"{shift.Name}: no price is configured for {day:yyyy-MM-dd}. Add an effective price before invoicing.");
            if (price!.Mode == "Monthly") monthlyDays.Add((day, price, active));
            else mixedBasisMonths.Add(day.ToString("yyyy-MM"));
        }
        var lines = new List<InvoiceLine>();
        foreach (var group in monthlyDays.GroupBy(d => (Month: d.Date.ToString("yyyy-MM"), d.Price, d.Plan.BranchId, d.Plan.ShiftName)))
        {
            var days = group.Select(d => d.Date).ToHashSet(); var first = days.Min(); var last = days.Max();
            var visitsInLine = visits.Where(v => v.BranchId == group.Key.BranchId && days.Contains(DateOnly.FromDateTime(TenantTimeZone.LocalDate(v.StartUtc, zone)))).ToArray();
            var calendarDays = DateTime.DaysInMonth(first.Year, first.Month);
            var wholeMonth = from <= new DateOnly(first.Year, first.Month, 1)
                && to >= new DateOnly(first.Year, first.Month, calendarDays)
                && !mixedBasisMonths.Contains(group.Key.Month);
            // A selected full calendar month is one fixed monthly charge, even when
            // the shift's first/last active day is inside that month. Split price/branch
            // segments proportionally; retain calendar-day proration for partial selections.
            var denominator = wholeMonth ? monthlyDays.Count(d => d.Date.Year == first.Year && d.Date.Month == first.Month) : calendarDays;
            lines.Add(new() { ShiftId = shift.Id, ShiftName = group.Key.ShiftName, BranchName = Branch(group.Key.BranchId),
                PricingMode = "Monthly", Month = group.Key.Month, PeriodFrom = first, PeriodTo = last,
                LineKey = $"{shift.Id:N}:{group.Key.Month}:{group.Key.Price.EffectiveFrom:yyyyMMdd}:{group.Key.BranchId:N}",
                Description = $"{group.Key.Price.RateNet:0.00} EUR × {days.Count}/{denominator}",
                Quantity = (decimal)days.Count / denominator, UnitPriceNet = group.Key.Price.RateNet,
                NetAmount = Round(group.Key.Price.RateNet * days.Count / denominator), VatRatePercent = group.Key.Price.VatRatePercent,
                Hours = visitsInLine.Sum(v => (decimal)(v.EndUtc - v.StartUtc).Ticks / TimeSpan.TicksPerHour), Occurrences = visitsInLine.Length });
        }
        var pricedVisits = visits.Select(v => (Visit: v, Date: DateOnly.FromDateTime(TenantTimeZone.LocalDate(v.StartUtc, zone))))
            .Where(v => v.Date >= from && v.Date <= to).Select(v => (v.Visit, v.Date, Price: Price(v.Date))).ToArray();
        if (pricedVisits.Any(v => v.Price is null)) Invalid($"{shift.Name}: a scheduled visit has no effective price. Add pricing before invoicing.");
        foreach (var group in pricedVisits.Where(v => v.Price!.Mode != "Monthly" && (v.Price.Mode != "PerOccurrence" || v.Visit.Completed))
            .Select(v => { var special = v.Price!.Mode == "PerOccurrence" ? profile.Overrides.FirstOrDefault(o => o.OccurrenceStartUtc == v.Visit.StartUtc) : null;
                return (v.Visit, v.Date, Price: v.Price!, Rate: special?.RateNet ?? v.Price!.RateNet, Reason: special?.Reason); })
            .GroupBy(v => (v.Price, v.Rate, v.Reason, v.Visit.BranchId, v.Visit.ShiftName)))
        {
            var hours = group.Sum(v => (decimal)(v.Visit.EndUtc - v.Visit.StartUtc).Ticks / TimeSpan.TicksPerHour);
            var quantity = group.Key.Price.Mode == "Hourly" ? hours : group.Count();
            lines.Add(new() { ShiftId = shift.Id, ShiftName = group.Key.ShiftName, BranchName = Branch(group.Key.BranchId),
                PricingMode = group.Key.Price.Mode, PeriodFrom = group.Min(v => v.Date), PeriodTo = group.Max(v => v.Date),
                LineKey = $"{shift.Id:N}:{group.Key.Price.EffectiveFrom:yyyyMMdd}:{group.Key.BranchId:N}:{lines.Count}",
                Quantity = quantity, UnitPriceNet = group.Key.Rate, NetAmount = Round(quantity * group.Key.Rate),
                VatRatePercent = group.Key.Price.VatRatePercent, Hours = hours, Occurrences = group.Count(), OverrideReason = group.Key.Reason });
        }
        foreach (var adjustment in overrides.Where(o => o.ShiftId == shift.Id))
        {
            var affected = lines.Where(l => l.Month == adjustment.Month && l.PricingMode == "Monthly").ToArray();
            if (affected.Length == 0) Invalid($"{shift.Name}: monthly override {adjustment.Month} has no monthly-priced active days in the selected range.");
            if (affected.Select(l => l.VatRatePercent).Distinct().Count() != 1) Invalid("A monthly override cannot combine different VAT rates. Invoice the price periods separately.");
            var first = affected[0];
            lines.RemoveAll(l => affected.Contains(l));
            lines.Add(new() { ShiftId = shift.Id, ShiftName = first.ShiftName, BranchName = string.Join(" / ", affected.Select(l => l.BranchName).Distinct()),
                PricingMode = "Monthly", Month = adjustment.Month, PeriodFrom = affected.Min(l => l.PeriodFrom), PeriodTo = affected.Max(l => l.PeriodTo),
                LineKey = $"{shift.Id:N}:{adjustment.Month}:override", Description = string.Join(" + ", affected.Select(l => l.Description)),
                Quantity = 1, UnitPriceNet = adjustment.AmountNet, NetAmount = adjustment.AmountNet, VatRatePercent = first.VatRatePercent,
                Hours = affected.Sum(l => l.Hours), Occurrences = affected.Sum(l => l.Occurrences), OverrideReason = adjustment.Reason.Trim() });
        }
        foreach (var line in lines) line.VatAmount = Round(line.NetAmount!.Value * line.VatRatePercent / 100);
        return lines.OrderBy(l => l.PeriodFrom).ThenBy(l => l.BranchName).ToList();
    }
}
