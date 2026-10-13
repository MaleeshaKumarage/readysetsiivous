using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Shifts;

public class ShiftAssignment : BaseDocument
{
    public Guid ShiftId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateTime AssignedAtUtc { get; set; }
    public bool IsActive { get; set; }
    public string? Note { get; set; }
    public DateTime? UnassignedAtUtc { get; set; }
    public List<ShiftAssignmentPeriod> Periods { get; set; } = new();
    public void EnsureHistory()
    {
        if (Periods.Count == 0) Periods.Add(new() { FromUtc = AssignedAtUtc, ToUtc = IsActive ? null : UnassignedAtUtc ?? UpdatedUtc });
    }
    public bool Overlaps(DateTime start, DateTime end) => Periods.Count > 0
        ? Periods.Any(p => p.FromUtc < end && (!p.ToUtc.HasValue || p.ToUtc > start))
        : AssignedAtUtc < end && (IsActive || (UnassignedAtUtc ?? UpdatedUtc) > start);

    public static ShiftAssignment Create(
        Guid shiftId,
        Guid employeeId,
        string? note)
    {
        if (shiftId == Guid.Empty)
            throw new ArgumentException("Shift id is mandatory.", nameof(shiftId));
        if (employeeId == Guid.Empty)
            throw new ArgumentException("Employee id is mandatory.", nameof(employeeId));

        return new ShiftAssignment
        {
            Id = Guid.NewGuid(),
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
            ShiftId = shiftId,
            EmployeeId = employeeId,
            AssignedAtUtc = DateTime.UtcNow,
            IsActive = true,
            Note = note?.Trim()
        };
    }
}

public class ShiftAssignmentPeriod { public DateTime FromUtc { get; set; } public DateTime? ToUtc { get; set; } }
