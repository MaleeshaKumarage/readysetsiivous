using CleaningSuite.Application.QualityCycle;

namespace CleaningSuite.Application.QualityCycle;

// Public-safe projection: no cleaner identity, form ID or submission token.
public record QualityCycleWorkTiming(DateTime ScheduledStartUtc, DateTime ScheduledEndUtc,
    DateTime? ActualStartUtc, DateTime? ActualEndUtc, decimal? StaffHours);
public record QualityCycleWorkTotals(IReadOnlyList<QualityCycleWorkTiming> Records, decimal? StaffHours, int IncompleteRecords);
public static class QualityCycleWorkProjection
{
    public static QualityCycleWorkTotals FromForms(IEnumerable<QualityCycleFormDto> source)
    {
        var records = source.GroupBy(x => x.Id)
            .Select(g => CleaningSuite.Application.Shifts.RecordedWork.Representative(g, x => x.IsSubmitted, x => x.StartedAtUtc, x => x.EndedAtUtc)!)
            .Where(x => x.IsSubmitted || x.StartedAtUtc.HasValue).Select(x => {
                var duration = CleaningSuite.Application.Shifts.RecordedWork.Duration(x.IsSubmitted, x.StartedAtUtc, x.EndedAtUtc);
                return new QualityCycleWorkTiming(x.ShiftOccurrenceUtc, x.ShiftOccurrenceEndUtc,
                    x.StartedAtUtc, x.EndedAtUtc, duration is { } d ? (decimal)d.TotalHours : null);
            }).OrderBy(x => x.ScheduledStartUtc).ThenBy(x => x.ActualStartUtc).ToList();
        var valid = records.Where(x => x.StaffHours.HasValue).ToList();
        return new(records, valid.Count > 0 ? valid.Sum(x => x.StaffHours!.Value) : null,
            records.Count(x => !x.StaffHours.HasValue));
    }
}
