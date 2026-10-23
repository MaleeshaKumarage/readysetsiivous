using CleaningSuite.Domain.Leads;
namespace CleaningSuite.Application.Leads;

public record LeadActor(string Id, string Name);
public record LeadAdminRecipient(string Id, string Name, string Email);
public record LeadAudioInfo(string Extension, string ContentType, double DurationSeconds);
public record LeadFollowUpInput(string Reason, string FirstDate, string? EndDate, string Recurrence, int Interval, List<string> RecipientIds);
public interface ILeadAdminDirectory { Task<IReadOnlyList<LeadAdminRecipient>> ListAsync(CancellationToken ct); }
public interface ILeadAudioProbe { Task<LeadAudioInfo> ValidateAsync(byte[] content, CancellationToken ct); }
public interface ILeadAudioStore
{
    Task SaveAsync(LeadVoiceNote note, byte[] content, CancellationToken ct);
    Task<byte[]?> ReadAsync(LeadVoiceNote note, CancellationToken ct);
    Task DeleteAsync(LeadVoiceNote note);
    Task<IReadOnlyList<(Guid LeadId, Guid NoteId, string Extension)>> UnreferencedCandidatesAsync(DateTime beforeUtc, CancellationToken ct);
}
public interface ILeadCollaborationRepository
{
    Task<LeadVoiceNote?> VoiceAsync(Guid leadId, Guid id, CancellationToken ct);
    Task<(IReadOnlyList<LeadVoiceNote> Items, int Total)> VoicesAsync(Guid leadId, int skip, int take, CancellationToken ct);
    Task SaveVoiceAsync(LeadVoiceNote note, CancellationToken ct);
    Task DeleteVoiceAsync(LeadVoiceNote note, CancellationToken ct);
    Task<LeadFollowUp?> FollowUpAsync(Guid leadId, Guid id, CancellationToken ct);
    Task<(IReadOnlyList<LeadFollowUp> Items, int Total)> FollowUpsAsync(Guid leadId, int skip, int take, CancellationToken ct);
    Task<IReadOnlyList<LeadFollowUpDelivery>> PendingAsync(Guid id, CancellationToken ct);
    Task<(IReadOnlyList<LeadFollowUpDelivery> Items, int Total)> HistoryAsync(Guid leadId, int skip, int take, CancellationToken ct);
    Task SaveAsync(LeadFollowUp followUp, IEnumerable<LeadFollowUpDelivery> deliveries, CancellationToken ct);
    Task<IReadOnlyList<(Guid LeadId, Guid Id)>> DueAsync(DateTime now, CancellationToken ct);
}
public interface ILeadReminderTransport
{
    Task SendAsync(LeadFollowUpDelivery delivery, CancellationToken ct);
}
public sealed class LeadReminderSendException(string message, bool retryable, TimeSpan? retryAfter = null) : Exception(message)
{
    public bool Retryable { get; } = retryable;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
