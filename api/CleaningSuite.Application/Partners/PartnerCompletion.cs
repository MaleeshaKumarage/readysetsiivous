using CleaningSuite.Domain.QualityCycle;

namespace CleaningSuite.Application.Partners;

/// <summary>
/// Partner-facing view of a completed visit. Deliberately omits employee ids and full names, shift internals,
/// email delivery state, work-time corrections, access tokens and the cleaner's identity.
/// </summary>
public sealed record PartnerCompletionDto(
    Guid RequestId,
    string BranchName,
    DateOnly FromDate,
    DateOnly ToDate,
    DateTime? PlannedStartUtc,
    DateTime? PlannedEndUtc,
    DateTime? ActualStartUtc,
    DateTime? ActualEndUtc,
    IReadOnlyList<PartnerCompletionReport> Reports,
    IReadOnlyList<PartnerChangeDto> Messages);

/// <summary>One submitted Quality Cycle checklist for the visit. A visit can carry more than one checklist.</summary>
public sealed record PartnerCompletionReport(
    string TemplateTitle,
    DateTime? StartedAtUtc,
    DateTime? EndedAtUtc,
    IReadOnlyList<PartnerCompletionTask> Tasks,
    string? CleanerNotes,
    IReadOnlyList<PartnerCompletionPhoto> Photos);

public sealed record PartnerCompletionTask(string Text, bool Done);

public sealed record PartnerCompletionPhoto(string Url, string Note);

public static class PartnerCompletionView
{
    /// <summary>Shows the first letter of the cleaner's first name, e.g. "T.". Null when no name is recorded.</summary>
    public static string? MaskCleaner(string? firstName)
    {
        var trimmed = firstName?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : $"{char.ToUpperInvariant(trimmed[0])}.";
    }

    public static PartnerCompletionReport Report(QualityCycleForm form)
    {
        // Photos recorded before per-photo notes existed only carry URLs; they are shown with an empty note.
        var photos = form.Photos.Count > 0
            ? form.Photos.Select(p => new PartnerCompletionPhoto(p.Url, p.Note))
            : form.PhotoUrls.Select(url => new PartnerCompletionPhoto(url, string.Empty));
        return new PartnerCompletionReport(
            form.TemplateTitle,
            form.StartedAtUtc,
            form.EndedAtUtc,
            form.Items.Select(item => new PartnerCompletionTask(item.ItemText, item.IsChecked)).ToArray(),
            string.IsNullOrWhiteSpace(form.CleanerNotes) ? null : form.CleanerNotes,
            photos.ToArray());
    }
}
