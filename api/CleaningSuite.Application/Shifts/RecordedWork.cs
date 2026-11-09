using CleaningSuite.Domain.QualityCycle;

namespace CleaningSuite.Application.Shifts;

/// <summary>
/// Single source of truth for a visit's recorded work time. Every screen that shows a duration
/// (calendar, completion review, quality-cycle work totals) must go through these rules so the
/// same visit never reports two different durations.
/// </summary>
public static class RecordedWork
{
    /// <summary>
    /// Picks the record that represents one cleaner's work on one occurrence: a submitted record
    /// beats an unfinished one, then the latest recorded end, then the latest start. Keeping this
    /// rule in one place stops a stray started-only record from hiding the completed one.
    /// </summary>
    public static QualityCycleForm? Representative(IEnumerable<QualityCycleForm> forms) =>
        forms
            .OrderByDescending(f => f.IsSubmitted)
            .ThenByDescending(f => f.EndedAtUtc ?? DateTime.MinValue)
            .ThenByDescending(f => f.StartedAtUtc ?? DateTime.MinValue)
            .FirstOrDefault();

    /// <summary>
    /// Recorded duration counts only for a submitted record with a valid start and end, so
    /// unsubmitted or inverted timings never produce a duration.
    /// </summary>
    public static TimeSpan? Duration(QualityCycleForm? form) =>
        form is { IsSubmitted: true, StartedAtUtc: DateTime start, EndedAtUtc: DateTime end } && end >= start
            ? end - start
            : null;
}
