namespace CleaningSuite.Application.Partners;

/// <summary>A nonblocking cross-replica reservation for one notification's provider attempts.</summary>
public interface IPartnerNotificationReservation
{
    Task<IAsyncDisposable?> TryAcquireAsync(Guid notificationId, CancellationToken ct = default);
}
