using CleaningSuite.Domain.Common;
using System.Security.Cryptography;
using System.Text;

namespace CleaningSuite.Domain.Partners;

/// <summary>
/// A partner's request for staff to collect or hand over keys at one property on one day. The fee is frozen
/// from the tenant setting when the request is submitted, so later fee changes never rewrite it.
/// </summary>
public sealed class PartnerKeyRequest : BaseDocument
{
    public const string KindPickup = "Pickup";
    public const string KindHandover = "Handover";
    public const string StatusRequested = "Requested";
    public const string StatusCompleted = "Completed";
    public const string StatusCancelled = "Cancelled";
    public const int MaxNotesLength = 1000;
    public const int MaxPhotos = 10;

    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public Guid RequestedByEmployeeId { get; set; }
    public Guid ClientRequestId { get; set; }
    public int Revision { get; set; } = 1;
    public string Kind { get; set; } = KindPickup;
    public DateOnly Date { get; set; }
    public string? Notes { get; set; }
    public decimal FeeNet { get; set; }
    public decimal VatRatePercent { get; set; }
    public string Status { get; set; } = StatusRequested;
    public string? CancellationReason { get; set; }
    /// <summary>The staff member who collects or hands over the keys. Staff-only: never shown to partners.</summary>
    public Guid? AssignedEmployeeId { get; set; }
    public DateTime? CompletedUtc { get; set; }
    /// <summary>Photos taken at completion, stored by the key request file store.</summary>
    public List<KeyRequestPhoto> Photos { get; set; } = new();

    public static Guid StableId(string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 16));
}

/// <summary>Metadata for one stored key request photo. The bytes live in the key request file store.</summary>
public sealed class KeyRequestPhoto
{
    public Guid Id { get; set; }
    public string Extension { get; set; } = ".jpg";
    public int SizeBytes { get; set; }
    public DateTime UploadedUtc { get; set; }
}
