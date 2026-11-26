using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Shifts;

/// <summary>
/// A manually created visit that was cancelled. The visit itself is removed from the shift, so work lists, invoices
/// and rosters no longer see it, and a copy is kept here so it can be restored unchanged: same id, number and offer.
/// The id is the visit's own id.
/// </summary>
public class CancelledVisit : BaseDocument
{
    public Guid ShiftId { get; set; }
    /// <summary>The visit's start, copied so cancelled visits can be listed by date.</summary>
    public DateTime StartUtc { get; set; }
    public ShiftOccurrenceException Visit { get; set; } = new();
    /// <summary>CustomerCancelled or CompanyCancelled.</summary>
    public ShiftOccurrenceExceptionStatus CancelStatus { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime CancelledUtc { get; set; }

    public static CancelledVisit From(ShiftOccurrenceException visit, ShiftOccurrenceExceptionStatus status, string reason, DateTime nowUtc)
    {
        if (visit.Status != ShiftOccurrenceExceptionStatus.Extra) throw new ArgumentException("Only a manually created visit is cancelled this way.", nameof(visit));
        if (status is not (ShiftOccurrenceExceptionStatus.CustomerCancelled or ShiftOccurrenceExceptionStatus.CompanyCancelled))
            throw new ArgumentException("Choose customer cancelled or company cancelled.", nameof(status));
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Reason is mandatory.", nameof(reason));
        return new CancelledVisit
        {
            Id = visit.Id, ShiftId = visit.ShiftId, StartUtc = visit.StartUtc, Visit = visit, CancelStatus = status,
            Reason = reason.Trim(), CancelledUtc = nowUtc, CreatedUtc = nowUtc, UpdatedUtc = nowUtc,
        };
    }
}
