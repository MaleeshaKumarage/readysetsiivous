namespace CleaningSuite.Application.Notifications;

/// <summary>
/// The events a person can switch on or off in their profile. Keys are the push and inbox kinds the app sends.
/// Cleaner events go to active employees; staff events go to administrators and managers.
/// </summary>
public static class NotificationEvents
{
    public const string CleanerGroup = "Cleaner";
    public const string StaffGroup = "Staff";

    public sealed record Definition(string Key, string Group, string Label, string Description, bool EmailAvailable);

    public static readonly IReadOnlyList<Definition> All =
    [
        new("ShiftAssigned", CleanerGroup, "Assigned to a shift", "A visit or shift is given to you.", false),
        new("ShiftRemoved", CleanerGroup, "Removed from a shift", "A visit or shift is taken off your schedule.", false),
        new("ShiftUpdated", CleanerGroup, "Shift changed", "Times or details of a shift you are on have changed.", false),
        new("VisitOffer", CleanerGroup, "Visit offer", "A visit is offered to you to accept or decline.", false),
        new("PartnerOffer", CleanerGroup, "Partner cleaning offer", "A partner cleaning is offered to you to approve.", false),
        new("WorkAutoAccepted", CleanerGroup, "Work accepted automatically", "Work was accepted for you because automatic acceptance is on.", false),
        new("PartnerRequest", StaffGroup, "New partner request", "A partner has sent a cleaning request to review.", false),
        new("StaffPartnerAccepted", StaffGroup, "Cleaner accepted partner work", "A cleaner accepted a partner cleaning.", false),
        new("StaffPartnerOfferDeclined", StaffGroup, "Cleaner declined partner offer", "A cleaner declined a partner cleaning offer.", false),
        new("StaffPartnerRequestChanged", StaffGroup, "Partner changed a request", "A partner changed a cleaning request.", false),
        new("StaffPartnerRequestCancelled", StaffGroup, "Partner cancelled a request", "A partner cancelled a cleaning request.", false),
        new("StaffVisitOfferDeclined", StaffGroup, "Cleaner declined a visit", "A cleaner declined a visit offer.", false),
    ];

    public static bool IsScheduleKind(string kind) => kind is "ShiftAssigned" or "ShiftRemoved" or "ShiftUpdated" or "ScheduleChanged";

    /// <summary>Alerts queued before the split were sent as "ScheduleChanged"; they count as a shift change.</summary>
    public static string Normalize(string kind) => kind == "ScheduleChanged" ? "ShiftUpdated" : kind;

    public static IEnumerable<Definition> For(bool cleaner, bool staff) =>
        All.Where(e => (cleaner && e.Group == CleanerGroup) || (staff && e.Group == StaffGroup));
}
