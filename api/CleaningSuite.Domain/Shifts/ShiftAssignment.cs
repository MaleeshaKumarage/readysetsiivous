using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Shifts;

public class ShiftAssignment : BaseDocument
{
    public Guid ShiftId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateTime AssignedAtUtc { get; set; }
    public bool IsActive { get; set; }
    public string? Note { get; set; }

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
