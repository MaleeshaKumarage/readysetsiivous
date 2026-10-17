using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Shifts;

/// <summary>Reporting snapshot, separate from the live shift and roster.</summary>
public class DeletedShift : BaseDocument
{
    public Shift Snapshot { get; set; } = new();
    public List<ShiftAssignment> Assignments { get; set; } = new();
    public List<ShiftOccurrenceException> OccurrenceExceptions { get; set; } = new();
    public DateTime DeletedUtc { get; set; } = DateTime.UtcNow;
}
