using System.Linq.Expressions;
using CleaningSuite.Domain.Bookings;
using CleaningSuite.Domain.Partners;
using MediatR;

namespace CleaningSuite.Application.WorkQueue;

public static class WorkQueueViews
{
    public const string NeedsAction = "needs-action";
    public const string Scheduled = "scheduled";
    public const string InProgress = "in-progress";
    public const string Completed = "completed";

    public static readonly string[] All = [NeedsAction, Scheduled, InProgress, Completed];

    public static bool IsKnown(string? view) => view is not null && All.Contains(view);
}

/// <summary>Maps stored statuses to queue views. In-progress has no stored state yet, so it is always empty.</summary>
public static class WorkQueueRules
{
    public static Expression<Func<Booking, bool>> BookingIn(string view) => view switch
    {
        WorkQueueViews.NeedsAction => b => b.Status == Booking.StatusNew || (b.Status == Booking.StatusConfirmed && b.EmployeeId == null),
        WorkQueueViews.Scheduled => b => b.Status == Booking.StatusConfirmed && b.EmployeeId != null,
        WorkQueueViews.Completed => b => b.Status == Booking.StatusCompleted || b.Status == Booking.StatusCancelled,
        _ => b => false,
    };

    public static Expression<Func<PartnerCleaningRequest, bool>> PartnerIn(string view) => view switch
    {
        // An offered request still waits on a cleaner, so it stays in Needs action until accepted.
        WorkQueueViews.NeedsAction => r => r.Status == PartnerCleaningRequest.StatusRequested || r.Status == PartnerCleaningRequest.StatusOffered,
        WorkQueueViews.Scheduled => r => r.Status == PartnerCleaningRequest.StatusAccepted,
        WorkQueueViews.Completed => r => r.Status == PartnerCleaningRequest.StatusCancelled,
        _ => r => false,
    };

    public static string BookingLabel(Booking booking) => booking.Status switch
    {
        Booking.StatusNew => "New booking",
        Booking.StatusConfirmed when booking.EmployeeId is null => "Unassigned",
        Booking.StatusConfirmed => "Confirmed",
        Booking.StatusCompleted => "Completed",
        Booking.StatusCancelled => "Cancelled",
        _ => booking.Status,
    };

    public static string PartnerLabel(PartnerCleaningRequest request) => request.Status switch
    {
        PartnerCleaningRequest.StatusRequested => "Unassigned",
        PartnerCleaningRequest.StatusOffered => "Awaiting acceptance",
        PartnerCleaningRequest.StatusAccepted => "Accepted",
        PartnerCleaningRequest.StatusCancelled => "Cancelled",
        _ => request.Status,
    };
}

public record WorkQueueQuery(string View, string Source, string? Search, int Skip, int Take) : IRequest<WorkQueuePage>;

public record WorkQueueItem(
    string Source,
    Guid Id,
    string Reference,
    string CustomerName,
    string ScheduledLocal,
    string Status,
    string StateLabel,
    string? AssigneeName,
    string Href);

public record WorkQueuePage(
    string View,
    string Source,
    IReadOnlyList<WorkQueueItem> Items,
    int Total,
    IReadOnlyDictionary<string, int> Counts);

public interface IWorkQueueReader
{
    Task<WorkQueuePage> ReadAsync(WorkQueueQuery query, CancellationToken ct = default);
}

public sealed class WorkQueueHandler(IWorkQueueReader reader) : IRequestHandler<WorkQueueQuery, WorkQueuePage>
{
    public Task<WorkQueuePage> Handle(WorkQueueQuery request, CancellationToken ct) => reader.ReadAsync(request, ct);
}
