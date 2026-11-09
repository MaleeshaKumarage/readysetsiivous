using CleaningSuite.Domain.QualityCycle;

namespace CleaningSuite.Application.Shifts;

/// <summary>
/// Single source of truth for a visit's recorded work time. Every screen and total that shows a duration
/// (admin calendar, completion review, quality-cycle work totals, the correction save target) must go
/// through these rules so the same visit never reports two different durations or targets.
/// </summary>
public static class RecordedWork
{
    /// <summary>
    /// Picks the record that represents one cleaner's work on one occurrence: a submitted record
    /// beats an unfinished one, then the latest recorded end, then the latest start.
    /// </summary>
    public static T? Representative<T>(IEnumerable<T> records, Func<T, bool> submitted, Func<T, DateTime?> started, Func<T, DateTime?> ended) =>
        records
            .OrderByDescending(submitted)
            .ThenByDescending(r => ended(r) ?? DateTime.MinValue)
            .ThenByDescending(r => started(r) ?? DateTime.MinValue)
            .FirstOrDefault();

    /// <summary>
    /// Recorded duration counts only for a submitted record with a valid start and end. An end before the start
    /// is not a duration.
    /// </summary>
    public static TimeSpan? Duration(bool submitted, DateTime? started, DateTime? ended) =>
        submitted && started is DateTime start && ended is DateTime end && end >= start ? end - start : null;

    public static QualityCycleForm? Representative(IEnumerable<QualityCycleForm> forms) =>
        Representative(forms, f => f.IsSubmitted, f => f.StartedAtUtc, f => f.EndedAtUtc);

    public static TimeSpan? Duration(QualityCycleForm? form) =>
        form is null ? null : Duration(form.IsSubmitted, form.StartedAtUtc, form.EndedAtUtc);
}
