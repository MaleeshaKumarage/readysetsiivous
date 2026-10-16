using CleaningSuite.Domain.Common;
using System.Security.Cryptography;

namespace CleaningSuite.Domain.Shifts;

public enum ShiftOccurrenceExceptionStatus
{
    Moved = 1,
    CustomerCancelled = 2,
    CompanyCancelled = 3,
    Extra = 4
}

/// <summary>
/// A durable exception to one generated occurrence. OriginalStartUtc identifies the
/// generated occurrence; it is null only for an extra visit.
/// </summary>
public class ShiftOccurrenceException : BaseDocument
{
    public Guid ShiftId { get; set; }
    public DateTime? OriginalStartUtc { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public ShiftOccurrenceExceptionStatus Status { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid? EmployeeId { get; set; }

    public static ShiftOccurrenceException Create(Guid shiftId, DateTime? originalStartUtc,
        DateTime startUtc, DateTime endUtc, ShiftOccurrenceExceptionStatus status,
        string reason, Guid? employeeId = null)
    {
        var item = new ShiftOccurrenceException();
        if (originalStartUtc.HasValue)
        {
            Span<byte> identity = stackalloc byte[32];
            shiftId.TryWriteBytes(identity[..16]);
            BitConverter.TryWriteBytes(identity[16..24], originalStartUtc.Value.ToUniversalTime().Ticks);
            var hash = SHA256.HashData(identity[..24]);
            item.Id = new Guid(hash.AsSpan(0, 16));
        }
        item.Update(shiftId, originalStartUtc, startUtc, endUtc, status, reason, employeeId);
        item.CreatedUtc = DateTime.UtcNow;
        return item;
    }

    public void Update(Guid shiftId, DateTime? originalStartUtc, DateTime startUtc,
        DateTime endUtc, ShiftOccurrenceExceptionStatus status, string reason,
        Guid? employeeId = null)
    {
        if (shiftId == Guid.Empty) throw new ArgumentException("Shift is mandatory.", nameof(shiftId));
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        if (status == ShiftOccurrenceExceptionStatus.Extra && originalStartUtc.HasValue)
            throw new ArgumentException("An extra visit cannot replace a scheduled occurrence.", nameof(originalStartUtc));
        if (status != ShiftOccurrenceExceptionStatus.Extra && !originalStartUtc.HasValue)
            throw new ArgumentException("Choose the scheduled occurrence to change.", nameof(originalStartUtc));
        if (endUtc <= startUtc) throw new ArgumentException("End time must be after start time.", nameof(endUtc));
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Reason is mandatory.", nameof(reason));

        ShiftId = shiftId;
        OriginalStartUtc = originalStartUtc?.ToUniversalTime();
        StartUtc = startUtc.ToUniversalTime();
        EndUtc = endUtc.ToUniversalTime();
        Status = status;
        Reason = reason.Trim();
        EmployeeId = employeeId;
        UpdatedUtc = DateTime.UtcNow;
    }
}
