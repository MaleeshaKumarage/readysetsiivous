using CleaningSuite.Domain.Common;
using System.Security.Cryptography;
using System.Text;

namespace CleaningSuite.Domain.Partners;

/// <summary>One requested visit, not a recurrence. Financial terms are frozen on submission.</summary>
public sealed class PartnerCleaningRequest : BaseDocument
{
    // Stored lifecycle values. InProgress and Completed are derived at read time from quality forms, never stored.
    public const string StatusRequested = "Requested";
    public const string StatusOffered = "Offered";
    public const string StatusAccepted = "Accepted";
    public const string StatusCancelled = "Cancelled";

    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public Guid RequestedByEmployeeId { get; set; }
    public Guid ClientRequestId { get; set; }
    public int Revision { get; set; } = 1;
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public string? ExactStartLocalTime { get; set; }
    public string? Notes { get; set; }
    /// <summary>"Deep" or "Normal". Null reads as Normal. Copied to the visit when the request is accepted.</summary>
    public string? CleaningType { get; set; }
    public decimal PriceNet { get; set; }
    public decimal VatRatePercent { get; set; }
    public int EstimatedDurationMinutes { get; set; }
    public int PricingVersion { get; set; }
    public string Status { get; set; } = StatusRequested;
    public DateTime? FirstAcceptedAtUtc { get; set; }
    public Guid? LinkedShiftId { get; set; }
    public DateTime? PlannedStartUtc { get; set; }
    public DateTime? PlannedEndUtc { get; set; }
    public Guid? AcceptedEmployeeId { get; set; }
    public Guid? OfferedEmployeeId { get; set; }
    public Guid? OfferId { get; set; }
    public DateTime? OfferPlannedStartUtc { get; set; }
    public string? CancellationReason { get; set; }
    /// <summary>
    /// Start of this request's visit on a branch main shift. Null while the request has no visit, and for
    /// requests accepted before main shifts existed (those own a whole shift, found through LinkedShiftId).
    /// </summary>
    public DateTime? OccurrenceStartUtc { get; set; }
    /// <summary>The visit number this request holds on its branch main shift. Kept when the visit is rescheduled.</summary>
    public int? OccurrenceSequence { get; set; }

    public static Guid StableId(string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 16));
}

public sealed class PartnerChangeRequest : BaseDocument
{
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public Guid? RequestId { get; set; }
    public Guid RequestedByEmployeeId { get; set; }
    public string Kind { get; set; } = "Property";
    public string Message { get; set; } = "";
    public DateTime? ResolvedUtc { get; set; }
    public string? Resolution { get; set; }
    // What the partner asks for. "Message" only needs a reply; the others can be applied by staff.
    public string Action { get; set; } = "Message";
    public DateOnly? ProposedFromDate { get; set; }
    public DateOnly? ProposedToDate { get; set; }
    public string? ProposedExactStartLocalTime { get; set; }
    public string? ProposedNotes { get; set; }
}

/// <summary>Committed together with the event; contains references, never property access instructions.</summary>
public sealed class PartnerNotification : BaseDocument
{
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public Guid? RequestId { get; set; }
    public Guid? RecipientEmployeeId { get; set; }
    public string Audience { get; set; } = "Staff";
    public string Kind { get; set; } = "";
    public int Revision { get; set; }
    public string State { get; set; } = "Pending";
    public int Attempts { get; set; }
    public DateTime NextAttemptUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SentUtc { get; set; }
    public DateTime? FirstAttemptUtc { get; set; }
    public List<PartnerNotificationDelivery> Deliveries { get; set; } = [];
    public DateTime? LeaseUntilUtc { get; set; }
    public string? LastError { get; set; }
}

public sealed class PartnerNotificationDelivery
{
    public Guid Id { get; set; }
    public Guid RecipientEmployeeId { get; set; }
    public string To { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Html { get; set; } = "";
    public DateTime? SkippedUtc { get; set; }
    public string? SkipReason { get; set; }
    public DateTime? FailedUtc { get; set; }
    public string? FailureReason { get; set; }
    public DateTime? SentUtc { get; set; }
}
