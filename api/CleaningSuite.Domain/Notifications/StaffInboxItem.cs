using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Notifications;

/// <summary>An event an admin or manager should act on, kept in the app so it is seen even when push is off.</summary>
public class StaffInboxItem : BaseDocument
{
    public Guid EmployeeId { get; set; }
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string Link { get; set; } = "";
    public DateTime? ReadUtc { get; set; }
}
