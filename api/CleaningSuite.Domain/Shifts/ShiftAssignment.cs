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

    public bool CoversVisit(DateTime start, DateTime end, DateTime? shiftStartUtc = null) => Periods.Count > 0
        ? Periods.Any(p => p.FromUtc <= start && (!p.ToUtc.HasValue || start < p.ToUtc.Value))
        : CoversOccurrence(start, end, shiftStartUtc);

    public bool CoversOccurrence(DateTime start, DateTime end, DateTime? shiftStartUtc = null) =>
        Periods.Count == 0 && IsActive && shiftStartUtc.HasValue ? shiftStartUtc.Value < end : Overlaps(start, end);

    public void EndFrom(DateTime boundaryUtc, DateTime nowUtc)
    {
        EnsureHistory();
        Periods = Periods.Where(p => p.FromUtc < boundaryUtc).Select(p => new ShiftAssignmentPeriod {
            FromUtc = p.FromUtc, ToUtc = p.ToUtc.HasValue && p.ToUtc.Value < boundaryUtc ? p.ToUtc : boundaryUtc
        }).ToList();
        IsActive = Periods.Any(p => !p.ToUtc.HasValue || p.ToUtc > nowUtc);
        UnassignedAtUtc = boundaryUtc; UpdatedUtc = nowUtc;
    }

    public void BeginFrom(DateTime boundaryUtc, DateTime nowUtc, string reason)
    {
        if (Periods.Count == 0 && AssignedAtUtc < boundaryUtc && (IsActive || UnassignedAtUtc.HasValue)) EnsureHistory();
        if (!Periods.Any(p => p.FromUtc <= boundaryUtc && !p.ToUtc.HasValue))
            Periods.Add(new() { FromUtc = boundaryUtc });
        IsActive = true; UnassignedAtUtc = null; Note = reason; UpdatedUtc = nowUtc;
    }

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
