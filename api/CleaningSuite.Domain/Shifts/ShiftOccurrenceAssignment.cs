using System.Security.Cryptography;
using System.Text;
using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Shifts;

public enum OccurrenceAssignmentMode { Inherit = 0, Replace = 1, Add = 2 }

/// <summary>A roster override, independent from a visit's move/cancellation.</summary>
public sealed class ShiftOccurrenceAssignment : BaseDocument
{
    public Guid ShiftId { get; set; }
    public DateTime? OriginalStartUtc { get; set; }
    public Guid? ExtraExceptionId { get; set; }
    public OccurrenceAssignmentMode Mode { get; set; }
    public List<Guid> EmployeeIds { get; set; } = new();
    public string Reason { get; set; } = "";

    public static Guid Identity(Guid shiftId, DateTime? originalStartUtc, Guid? extraExceptionId)
    {
        if (shiftId == Guid.Empty || originalStartUtc.HasValue == extraExceptionId.HasValue
            || extraExceptionId == Guid.Empty)
            throw new ArgumentException("Choose one scheduled occurrence or extra visit.");
        var key = originalStartUtc.HasValue ? $"scheduled:{Utc(originalStartUtc.Value).Ticks}"
            : $"extra:{extraExceptionId:N}";
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"roster:{shiftId:N}:{key}"))[..16]);
    }

    public static ShiftOccurrenceAssignment Create(Guid shiftId, DateTime? originalStartUtc, Guid? extraExceptionId,
        OccurrenceAssignmentMode mode, IEnumerable<Guid> employeeIds, string reason)
    {
        var item = new ShiftOccurrenceAssignment { Id = Identity(shiftId, originalStartUtc, extraExceptionId),
            ShiftId = shiftId, OriginalStartUtc = originalStartUtc.HasValue
                ? Utc(originalStartUtc.Value) : null, ExtraExceptionId = extraExceptionId };
        item.Update(mode, employeeIds, reason);
        return item;
    }

    public void Update(OccurrenceAssignmentMode mode, IEnumerable<Guid> employeeIds, string reason)
    {
        var ids = employeeIds.Distinct().Order().ToList();
        if (!Enum.IsDefined(mode) || ids.Any(id => id == Guid.Empty)
            || (mode != OccurrenceAssignmentMode.Inherit && ids.Count == 0))
            throw new ArgumentException("Choose active employees and a supported assignment action.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
            throw new ArgumentException("An assignment reason of at most 500 characters is required.");
        Mode = mode; EmployeeIds = mode == OccurrenceAssignmentMode.Inherit ? new() : ids;
        Reason = reason.Trim(); UpdatedUtc = DateTime.UtcNow;
    }

    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Local
        ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
