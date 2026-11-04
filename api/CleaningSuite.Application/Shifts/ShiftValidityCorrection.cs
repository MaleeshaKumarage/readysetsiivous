using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;

namespace CleaningSuite.Application.Shifts;

public enum ShiftValidityFindingAction { Remove, Report }

public sealed record ShiftValidityFinding(Guid ShiftId, string Kind, DateTime OccurrenceUtc, ShiftValidityFindingAction Action, string Reason, Guid? DocumentId = null);

/// <summary>
/// Plans the one-time correction of stored records that point past a shift's ValidUntil. Only records that
/// produce no visit and have no work evidence are removed. Everything else is reported for a person to review.
/// </summary>
public static class ShiftValidityCorrection
{
    public const string RemovedRosterOverride = "RosterOverride";
    public const string RemovedOccurrenceException = "OccurrenceException";
    public const string ReportedExtraVisit = "ExtraVisit";
    public const string ReportedRosterOverride = "RosterOverrideWithEvidence";
    public const string ReportedMovedIntoWindow = "MovedVisit";

    public static IReadOnlyList<ShiftValidityFinding> Plan(
        IEnumerable<Shift> shifts,
        IEnumerable<ShiftOccurrenceAssignment> overrides,
        IEnumerable<ShiftOccurrenceException> exceptions,
        IEnumerable<QualityCycleForm> forms,
        TimeZoneInfo zone)
    {
        var endByShift = shifts.Where(shift => shift.ValidUntil.HasValue)
            .ToDictionary(shift => shift.Id, shift => DateOnly.FromDateTime(shift.ValidUntil!.Value.Date));
        var evidence = forms.Where(form => endByShift.ContainsKey(form.ShiftId))
            .Select(form => (form.ShiftId, form.ShiftOccurrenceUtc.Ticks)).ToHashSet();
        bool HasEvidence(Guid shiftId, DateTime? original, DateTime start) =>
            (original.HasValue && evidence.Contains((shiftId, original.Value.Ticks))) || evidence.Contains((shiftId, start.Ticks));
        DateOnly LocalDate(DateTime utc) => DateOnly.FromDateTime(TenantTimeZone.LocalDate(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone));
        var findings = new List<ShiftValidityFinding>();

        foreach (var item in overrides)
        {
            // Assignments on extra visits follow their extra-visit exception, which is reported on its own.
            if (item.ExtraExceptionId.HasValue || !endByShift.TryGetValue(item.ShiftId, out var end) || item.OriginalStartUtc is not { } original) continue;
            if (LocalDate(original) <= end) continue;
            if (HasEvidence(item.ShiftId, original, original))
                findings.Add(new(item.ShiftId, ReportedRosterOverride, original, ShiftValidityFindingAction.Report, "Assignment on a visit with work evidence; kept."));
            else
                findings.Add(new(item.ShiftId, RemovedRosterOverride, original, ShiftValidityFindingAction.Remove, "Assignment on a visit the shift no longer schedules.", item.Id));
        }

        foreach (var item in exceptions)
        {
            if (!endByShift.TryGetValue(item.ShiftId, out var end)) continue;
            var start = item.StartUtc;
            if (item.Status == ShiftOccurrenceExceptionStatus.Extra)
            {
                if (LocalDate(start) > end)
                    findings.Add(new(item.ShiftId, ReportedExtraVisit, start, ShiftValidityFindingAction.Report, "Extra visit added beyond the shift end; review."));
                continue;
            }
            var originalBeyond = item.OriginalStartUtc is { } original && LocalDate(original) > end;
            var startBeyond = LocalDate(start) > end;
            if (!startBeyond)
            {
                // A visit still inside the window: a moved-in visit is kept and reported, a cancellation inside is left alone.
                if (originalBeyond)
                    findings.Add(new(item.ShiftId, ReportedMovedIntoWindow, item.OriginalStartUtc!.Value, ShiftValidityFindingAction.Report, "Visit moved into the shift window; kept."));
                continue;
            }
            if (HasEvidence(item.ShiftId, item.OriginalStartUtc, start))
            {
                findings.Add(new(item.ShiftId, ReportedMovedIntoWindow, item.OriginalStartUtc ?? start, ShiftValidityFindingAction.Report, "Visit with work evidence beyond the shift end; kept."));
            }
            else if (originalBeyond || item.OriginalStartUtc is null)
            {
                findings.Add(new(item.ShiftId, RemovedOccurrenceException, item.OriginalStartUtc ?? start, ShiftValidityFindingAction.Remove, "Cancelled or moved-away visit the shift no longer schedules.", item.Id));
            }
            else
            {
                findings.Add(new(item.ShiftId, ReportedMovedIntoWindow, item.OriginalStartUtc.Value, ShiftValidityFindingAction.Report, "Visit moved from inside the window to beyond the shift end; review."));
            }
        }

        return findings;
    }
}
