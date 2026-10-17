using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Domain.QualityCycle;

namespace CleaningSuite.Application.QualityCycle;

public static class QualityCycleOccurrences
{
    public static async Task<IReadOnlyList<(QualityCyclePlan Plan, ShiftOccurrence Occurrence, IReadOnlyList<Guid> EmployeeIds)>> ResolvedInWindowAsync(
        IShiftRepository shifts, Shift shift, DateTime from, DateTime to, TimeZoneInfo zone, CancellationToken ct)
    {
        var exceptions = await shifts.ListOccurrenceExceptionsAsync(shift.Id, from, to, ct) ?? [];
        var planned = InWindow(shift, from, to, zone).ToList();
        // Include the original plan of inbound moves, even across a month boundary.
        foreach (var change in exceptions.Where(x => x.OriginalStartUtc.HasValue))
        {
            var original = change.OriginalStartUtc!.Value;
            if (!planned.Any(x => x.Occurrence.StartUtc == original))
                planned.AddRange(InWindow(shift, original, original.AddTicks(1), zone));
        }
        var history = await shifts.ListAssignmentHistoryAsync(shift.Id, ct) ?? [];
        var overrides = await shifts.ListOccurrenceAssignmentsAsync(shift.Id, ct) ?? [];
        var visits = ShiftVisitRoster.Resolve(shift.Id, planned.Select(x => x.Occurrence), exceptions, history, overrides,
            legacyStartUtc: shift.ValidFrom.HasValue ? TenantTimeZone.StartOfLocalDay(shift.ValidFrom.Value.Date, zone) : null);
        var result = new List<(QualityCyclePlan, ShiftOccurrence, IReadOnlyList<Guid>)>();
        foreach (var visit in visits.Where(x => x.Occurrence.StartUtc >= from && x.Occurrence.StartUtc < to))
        {
            QualityCyclePlan? plan = visit.ExtraExceptionId.HasValue ? null
                : planned.FirstOrDefault(x => x.Occurrence.StartUtc == visit.OriginalStartUtc).Plan;
            if (visit.ExtraExceptionId.HasValue)
                plan = shift.QualityCyclePlans.Where(x => x.EffectiveFromUtc <= visit.Occurrence.StartUtc)
                    .OrderByDescending(x => x.EffectiveFromUtc).FirstOrDefault()
                    ?? (shift.QualityCyclePlans.Count == 0 ? new QualityCyclePlan { CompanyId = shift.CompanyId,
                        BranchId = shift.BranchId, ShiftName = shift.Name, TemplateId = shift.QualityCycleTemplateId, IsActive = shift.IsActive } : null);
            if (plan is null || !plan.IsActive || !plan.TemplateId.HasValue) continue;
            result.Add((plan, new ShiftOccurrence { StartUtc = visit.Occurrence.StartUtc, EndUtc = visit.Occurrence.EndUtc }, visit.EmployeeIds));
        }
        return result;
    }

    public static IReadOnlyList<(QualityCyclePlan Plan, ShiftOccurrence Occurrence)> InWindow(
        Shift shift, DateTime fromUtc, DateTime toUtc, TimeZoneInfo? timeZone = null)
    {
        timeZone ??= TimeZoneInfo.Utc;
        var plans = shift.QualityCyclePlans.Count > 0 ? shift.QualityCyclePlans.OrderBy(p => p.EffectiveFromUtc).ToList()
            : new List<QualityCyclePlan> { new() {
                EffectiveFromUtc = shift.CreatedUtc, CompanyId = shift.CompanyId, BranchId = shift.BranchId,
                ShiftName = shift.Name, Schedule = shift.Schedule, TemplateId = shift.QualityCycleTemplateId,
                IsActive = shift.IsActive, ValidFrom = shift.ValidFrom, ValidUntil = shift.ValidUntil
            } };
        var result = new List<(QualityCyclePlan, ShiftOccurrence)>();
        for (var i = 0; i < plans.Count; i++)
        {
            var plan = plans[i];
            if (!plan.IsActive || !plan.TemplateId.HasValue) continue;
            var next = i + 1 < plans.Count ? plans[i + 1].EffectiveFromUtc : DateTime.MaxValue;
            var localFrom = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc), timeZone).Date.AddDays(-1);
            var localTo = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(toUtc, DateTimeKind.Utc), timeZone).Date.AddDays(1);
            foreach (var occurrence in ShiftScheduleCalculator.Calculate(plan.Schedule, localFrom, localTo, timeZone))
            {
                // An assignment/template attached during the shift may still produce its task,
                // but already-finished occurrences are never retroactively invented.
                if (occurrence.StartUtc < fromUtc || occurrence.StartUtc >= toUtc
                    || occurrence.EndUtc <= plan.EffectiveFromUtc || occurrence.StartUtc >= next) continue;
                var localOccurrenceDate = TimeZoneInfo.ConvertTimeFromUtc(occurrence.StartUtc, timeZone).Date;
                if (plan.ValidFrom.HasValue && localOccurrenceDate < plan.ValidFrom.Value.Date) continue;
                if (plan.ValidUntil.HasValue && localOccurrenceDate > plan.ValidUntil.Value.Date) continue;
                result.Add((plan, occurrence));
            }
        }
        return result.GroupBy(x => x.Item2.StartUtc).Select(g => g.First()).OrderBy(x => x.Item2.StartUtc).ToList();
    }
}
