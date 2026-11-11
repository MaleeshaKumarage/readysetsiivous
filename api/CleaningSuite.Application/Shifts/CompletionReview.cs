namespace CleaningSuite.Application.Shifts;

/// <summary>
/// Server-side review rules for past visits. Flags only guide an admin to a reasoned
/// correction; they never change work times. Eligibility must match the calendar's
/// correction boundary so that every flag has an available action.
/// </summary>
public static class CompletionReview
{
    public const string MissingCompletion = "missing-completion";
    public const string UnusualDuration = "unusual-duration";

    /// <summary>
    /// Product threshold, not a settled business rule: a recorded duration below half or
    /// above one and a half times the scheduled duration needs review. Tune here only.
    /// </summary>
    public const double MinDurationRatio = 0.5;
    public const double MaxDurationRatio = 1.5;

    public sealed record Flag(string Kind, string Label);

    /// <param name="eligible">Past day, not cancelled, not ambiguous, not partner, active work-role employee.</param>
    /// <param name="submitted">A checklist form exists and was submitted (times recorded as completed).</param>
    /// <param name="hasReasonedCorrection">An admin already recorded a reasoned correction whose times equal the current actual times.</param>
    public static Flag? Evaluate(bool eligible, DateTime scheduledStartUtc, DateTime scheduledEndUtc, bool submitted,
        DateTime? actualStartUtc, DateTime? actualEndUtc, bool hasReasonedCorrection)
    {
        if (!eligible) return null;
        if (!submitted)
            return new(MissingCompletion, actualStartUtc.HasValue ? "Started, not ended" : "No completion recorded");
        if (hasReasonedCorrection || actualStartUtc is null || actualEndUtc is null) return null;
        // An end before the start is not a duration (RecordedWork.Duration rejects it), so it needs the same correction.
        if (actualEndUtc < actualStartUtc) return new(MissingCompletion, "Recorded end is before start");

        var scheduled = (scheduledEndUtc - scheduledStartUtc).TotalMinutes;
        var actual = (actualEndUtc.Value - actualStartUtc.Value).TotalMinutes;
        if (scheduled <= 0) return null;
        var ratio = actual / scheduled;
        return ratio < MinDurationRatio || ratio > MaxDurationRatio
            ? new(UnusualDuration, "Actual duration differs from scheduled")
            : null;
    }
}
