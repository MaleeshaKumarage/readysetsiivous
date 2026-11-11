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
    /// <summary>The partner booking this visit was created for, when it sits on a branch main shift.</summary>
    public Guid? PartnerRequestId { get; set; }
    /// <summary>Visit number on its branch main shift. Given once, never reused.</summary>
    public int? Sequence { get; set; }
    /// <summary>Where the visit came from: <see cref="SourceAdminPanel"/> or <see cref="SourcePartnerPortal"/>.</summary>
    public string? Source { get; set; }
    /// <summary>Instructions for this visit only. The shift's own notes apply when this is null.</summary>
    public string? Notes { get; set; }
    /// <summary>Photo sections required for this visit only. The shift's own list applies when this is null.</summary>
    public List<string>? RequiredPhotoLabels { get; set; }

    /// <summary>The cleaner this visit is waiting on. The visit is nobody's work until the offer is accepted.</summary>
    public Guid? OfferedEmployeeId { get; set; }
    public Guid? OfferId { get; set; }
    /// <summary>Counts every offer change, so an answer to an older offer is refused.</summary>
    public int OfferRevision { get; set; }

    public const string SourceAdminPanel = "AdminPanel";
    public const string SourcePartnerPortal = "PartnerPortal";

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
