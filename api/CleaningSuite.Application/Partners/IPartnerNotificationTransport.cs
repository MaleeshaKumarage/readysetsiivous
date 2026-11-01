using CleaningSuite.Domain.Partners;
namespace CleaningSuite.Application.Partners;

public interface IPartnerNotificationTransport
{
    Task SendAsync(PartnerNotificationDelivery delivery, CancellationToken ct);
}
public sealed class PartnerNotificationSendException(string message, bool retryable, TimeSpan? retryAfter = null) : Exception(message)
{
    public bool Retryable { get; } = retryable;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
