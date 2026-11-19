using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Shifts;

/// <summary>
/// The state of each assignment just before the stale-assignment repair released it (#367). Written in the same
/// save as the release, so every released assignment can be restored by hand from this record.
/// </summary>
public class StaleAssignmentBackup : BaseDocument
{
    public DateTime TakenUtc { get; set; }
    public List<StaleAssignmentSnapshot> Assignments { get; set; } = new();
}

public class StaleAssignmentSnapshot
{
    public Guid AssignmentId { get; set; }
    public Guid ShiftId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateTime AssignedAtUtc { get; set; }
    public bool IsActive { get; set; }
    public string? Note { get; set; }
    public DateTime? UnassignedAtUtc { get; set; }
    public List<ShiftAssignmentPeriod> Periods { get; set; } = new();
}
