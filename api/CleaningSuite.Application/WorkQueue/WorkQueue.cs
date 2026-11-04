using System.Linq.Expressions;
using CleaningSuite.Domain.Bookings;
using CleaningSuite.Domain.Partners;
using CleaningSuite.Domain.QualityCycle;
using MediatR;

namespace CleaningSuite.Application.WorkQueue;

public static class WorkQueueViews
{
    public const string NeedsAction = "needs-action";
    public const string Scheduled = "scheduled";
    public const string InProgress = "in-progress";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";

    public static readonly string[] All = [NeedsAction, Scheduled, InProgress, Completed, Cancelled];

    public static bool IsKnown(string? view) => view is not null && All.Contains(view);
}

public static class WorkQueueLimits
{
    /// <summary>Deepest offset served. Each source reads skip+take rows, so this bounds the merge window.</summary>
    public const int MaxSkip = 10_000;
}

/// <summary>
/// One place for all bookings. Needs action waits for a cleaner; Scheduled is accepted or assigned work;
/// In progress has started and not finished; Completed means the work is done, however it was ended;
/// Cancelled is kept apart from Completed.
/// </summary>
public static class WorkQueueRules
{
    public static Expression<Func<Booking, bool>> BookingIn(string view) => view switch
    {
        WorkQueueViews.NeedsAction => b => b.Status == Booking.StatusNew || (b.Status == Booking.StatusConfirmed && b.EmployeeId == null),
        WorkQueueViews.Scheduled => b => b.Status == Booking.StatusConfirmed && b.EmployeeId != null,
        // Bookings have no stored in-progress state, so this view is empty for them.
        WorkQueueViews.Completed => b => b.Status == Booking.StatusCompleted,
        WorkQueueViews.Cancelled => b => b.Status == Booking.StatusCancelled,
        _ => b => false,
    };

    /// <summary>
    /// The view of a partner visit. Completion is derived from its quality forms, because a finished visit is not
    /// stored as completed. Returns null for statuses the queue does not show.
    /// </summary>
    public static string? PartnerView(PartnerCleaningRequest request, IReadOnlyList<QualityCycleForm> forms)
    {
        if (request.Status == PartnerCleaningRequest.StatusCancelled) return WorkQueueViews.Cancelled;
        if (request.Status is PartnerCleaningRequest.StatusRequested or PartnerCleaningRequest.StatusOffered) return WorkQueueViews.NeedsAction;
        if (request.Status != PartnerCleaningRequest.StatusAccepted) return null;
        var live = forms.Where(f => f.ShiftDeletedUtc is null).ToArray();
        if (live.Length > 0 && live.All(f => f.IsSubmitted)) return WorkQueueViews.Completed;
        return live.Any(f => f.StartedAtUtc.HasValue) ? WorkQueueViews.InProgress : WorkQueueViews.Scheduled;
    }

    public static string BookingLabel(Booking booking) => booking.Status switch
    {
        Booking.StatusNew => "New booking",
        Booking.StatusConfirmed when booking.EmployeeId is null => "Unassigned",
        Booking.StatusConfirmed => "Confirmed",
        Booking.StatusCompleted => "Completed",
        Booking.StatusCancelled => "Cancelled",
        _ => booking.Status,
    };

    public static string PartnerLabel(PartnerCleaningRequest request, string view) => view switch
    {
        WorkQueueViews.NeedsAction when request.Status == PartnerCleaningRequest.StatusOffered => "Awaiting acceptance",
        WorkQueueViews.NeedsAction => "Unassigned",
        WorkQueueViews.Scheduled => "Scheduled",
        WorkQueueViews.InProgress => "In progress",
        WorkQueueViews.Completed => "Completed",
        WorkQueueViews.Cancelled => "Cancelled",
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
