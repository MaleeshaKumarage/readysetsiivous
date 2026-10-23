using CleaningSuite.Domain.Common;
namespace CleaningSuite.Domain.Leads;

public sealed class LeadVoiceNote : BaseDocument
{
    public Guid LeadId { get; set; }
    public string AuthorId { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public string Extension { get; set; } = "";
    public string ContentType { get; set; } = "";
    public double DurationSeconds { get; set; }
}
public sealed class LeadFollowUp : BaseDocument
{
    public Guid LeadId { get; set; }
    public string Reason { get; set; } = "";
    public DateTime FirstDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string Recurrence { get; set; } = "Once";
    public int Interval { get; set; } = 1;
    public List<string> RecipientIds { get; set; } = [];
    public string AuthorId { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public Guid Revision { get; set; } = Guid.NewGuid();
    public string State { get; set; } = "Active";
    public DateTime? NextDate { get; set; }
    public DateTime NextCheckUtc { get; set; }
    public DateTime? LastCheckedUtc { get; set; }
    public string? LastError { get; set; }
}
public sealed class LeadFollowUpDelivery : BaseDocument
{
    public Guid LeadId { get; set; }
    public Guid FollowUpId { get; set; }
    public Guid Revision { get; set; }
    public string RecipientId { get; set; } = "";
    public string RecipientName { get; set; } = "";
    public string RecipientEmail { get; set; } = "";
    public DateTime LocalDate { get; set; }
    public string TimeZoneId { get; set; } = "";
    public DateTime DueUtc { get; set; }
    public string State { get; set; } = "Pending";
    public string Subject { get; set; } = "";
    public string Html { get; set; } = "";
    public int Attempts { get; set; }
    public DateTime? FirstAttemptUtc { get; set; }
    public DateTime? SentUtc { get; set; }
    public DateTime NextAttemptUtc { get; set; }
    public string? Error { get; set; }
}
