using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Notifications;

// One row per employee. Lists the event keys the person has switched off for the app (inbox and push).
// Every event is on by default, so a missing row means every event is on.
public sealed class NotificationPreference : BaseDocument
{
    public Guid EmployeeId { get; set; }
    public List<string> AppOff { get; set; } = [];
}
