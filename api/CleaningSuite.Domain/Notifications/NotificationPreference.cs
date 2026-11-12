using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Notifications;

// One row per employee. Lists the event keys the person has switched off for the app (inbox and push).
// Every event is on by default, so a missing row means every event is on.
public sealed class NotificationPreference : BaseDocument
{
    public Guid EmployeeId { get; set; }
    public List<string> AppOff { get; set; } = [];

    // One fixed id per employee, so two first-time saves update the same row instead of creating two.
    public static Guid IdFor(Guid employeeId) =>
        new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes($"notification-preference:{employeeId:N}")));
}
