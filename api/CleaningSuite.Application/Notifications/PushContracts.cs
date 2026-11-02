using CleaningSuite.Domain.Notifications;
using Marten;

namespace CleaningSuite.Application.Notifications;

public sealed record PushActor(Guid EmployeeId, string Subject);
public sealed record PushKeys(string P256dh, string Auth);
public sealed record PushSubscriptionInput(string Endpoint, PushKeys Keys);
public sealed record PushDeviceStatus(Guid Id, bool Enabled);
public sealed record PushConfiguration(bool Available, string? PublicKey, IReadOnlyList<PushDeviceStatus> Devices);
public sealed record PushTestResult(bool Accepted);
public sealed class PushSubscriptionException(string code) : Exception(code) { public string Code { get; } = code; }
public interface IPushSubscriptions
{
    Task<PushConfiguration> GetAsync(PushActor actor, CancellationToken ct = default);
    Task<PushDeviceStatus> EnrollAsync(PushActor actor, PushSubscriptionInput input, CancellationToken ct = default);
    Task DisableAsync(PushActor actor, Guid id, CancellationToken ct = default);
    Task<PushTestResult> TestAsync(PushActor actor, Guid id, CancellationToken ct = default);
}
public interface IPushOutbox
{
    Task StageScheduleChangeAsync(IDocumentSession session, Guid shiftId, IEnumerable<Guid> employeeIds, DateTimeOffset occurredAt, CancellationToken ct = default);
    Task StagePartnerRequestAsync(IDocumentSession session, Guid requestId, int revision, DateTimeOffset occurredAt, CancellationToken ct = default);
    Task StagePartnerOfferAsync(IDocumentSession session, Guid requestId, int revision, Guid offerId, Guid employeeId, DateTimeOffset occurredAt, CancellationToken ct = default);
    Task StageAsync(IDocumentSession session, PushIntent intent, CancellationToken ct = default);
}
public interface IPushDeliveryService { Task ProcessAsync(CancellationToken ct = default); }
public interface IPushDeliveryReservation { Task<IAsyncDisposable?> TryAcquireAsync(Guid id, CancellationToken ct = default); }
public interface IPushDeliveryEligibility { Task<bool> IsEligibleAsync(PushIntent intent, CancellationToken ct = default); }
public sealed record PushMessage(string Title, string Body, string Url, string Tag);
public sealed record PushSendResult(bool Accepted, bool Expired = false, bool Retryable = false, TimeSpan? RetryAfter = null);
public interface IPushTransport { Task<PushSendResult> SendAsync(PushDevice device, PushMessage message, DateTime expiresUtc, CancellationToken ct = default); }
public interface IPushSettings { bool Available { get; } string PublicKey { get; } string KeyId { get; } }
