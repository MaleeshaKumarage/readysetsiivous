using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Notifications;

// Credentials deliberately have no custom ToString or public response mapping.
public sealed class PushDevice : BaseDocument
{
    public Guid EmployeeId { get; set; }
    public string Subject { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string P256dh { get; set; } = "";
    public string Auth { get; set; } = "";
    public string VapidKeyId { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public DateTime OptedInUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastTestUtc { get; set; }
}

public sealed class PushIntent : BaseDocument
{
    public Guid EmployeeId { get; set; }
    public Guid? ShiftId { get; set; }
    public Guid? PartnerRequestId { get; set; }
    public int? RequestRevision { get; set; }
    public Guid? OfferId { get; set; }
    public DateTime? OriginalStartUtc { get; set; }
    public Guid? ExtraExceptionId { get; set; }
    public DateTime? OccurrenceStartUtc { get; set; }
    public DateTime? OccurrenceEndUtc { get; set; }
    public string EventKey { get; set; } = "";
    public string Kind { get; set; } = "ScheduleChanged";
    public bool AdminAudience { get; set; }
    public DateTime OccurredUtc { get; set; }
    public DateTime DueUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public bool Cancelled { get; set; }
    public bool Materialized { get; set; }
}

public sealed class PushDelivery : BaseDocument
{
    public Guid IntentId { get; set; }
    public Guid DeviceId { get; set; }
    public string State { get; set; } = "Pending";
    public int Attempts { get; set; }
    public DateTime NextAttemptUtc { get; set; }
    public DateTime? AttemptUtc { get; set; }
    public string? FailureCode { get; set; }
}
