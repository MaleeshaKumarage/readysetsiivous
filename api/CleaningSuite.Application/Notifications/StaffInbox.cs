namespace CleaningSuite.Application.Notifications;

/// <summary>The words an admin sees for each staff alert, in the inbox and in push.</summary>
public static class StaffAlertCopy
{
    public static bool Knows(string kind) => kind is "StaffPartnerAccepted" or "StaffPartnerOfferDeclined" or "StaffPartnerRequestChanged" or "StaffPartnerRequestCancelled" or "StaffVisitOfferDeclined";

    public static (string Title, string Body, string Link) For(string kind) => kind switch
    {
        "StaffPartnerAccepted" => ("Cleaner accepted partner work", "A cleaner accepted a partner cleaning. Open partner requests to check the plan.", "/admin/partner-requests/"),
        "StaffPartnerOfferDeclined" => ("Cleaner declined partner offer", "A cleaner declined a partner cleaning offer. Arrange another cleaner.", "/admin/partner-requests/"),
        "StaffPartnerRequestChanged" => ("Partner changed a request", "A partner changed a cleaning request. Review it in partner requests.", "/admin/partner-requests/"),
        "StaffPartnerRequestCancelled" => ("Partner cancelled a request", "A partner cancelled a cleaning request.", "/admin/partner-requests/"),
        "StaffVisitOfferDeclined" => ("Cleaner declined a visit", "A cleaner declined a visit offer. Assign another cleaner.", "/admin/shifts/"),
        _ => ("Notification", "Open the admin app to review it.", "/admin/"),
    };
}

public sealed record StaffInboxEntry(Guid Id, string Kind, string Title, string Body, string Link, DateTime CreatedUtc, bool Read);
public sealed record StaffInboxPage(int Unread, IReadOnlyList<StaffInboxEntry> Items);

public interface IStaffInbox
{
    Task<StaffInboxPage> ListAsync(Guid employeeId, CancellationToken ct = default);
    Task<bool> MarkReadAsync(Guid employeeId, Guid id, CancellationToken ct = default);
}
