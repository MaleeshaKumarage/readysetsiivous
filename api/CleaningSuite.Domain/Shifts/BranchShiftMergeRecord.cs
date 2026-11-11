using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Shifts;

/// <summary>
/// Kept for every shift that the branch shift merge folded into a main shift or removed. Its id is the old shift's id.
/// The backup holds every document the merge changed or deleted, as it was before, so the change can be explained or undone.
/// </summary>
public class BranchShiftMergeRecord : BaseDocument
{
    public const string OutcomeMerged = "Merged";
    public const string OutcomeRemoved = "Removed";

    public Guid BranchId { get; set; }
    public Guid? MainShiftId { get; set; }
    public Guid? PartnerRequestId { get; set; }
    public int? VisitNumber { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime AppliedUtc { get; set; }
    /// <summary>JSON of the old shift, its visit, assignments, billing profile, request and re-pointed work records.</summary>
    public string BackupJson { get; set; } = string.Empty;
}
