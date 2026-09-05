using CleaningSuite.Domain.Invoicing;

namespace CleaningSuite.Application.Invoicing;

public interface IInvoiceRepository
{
    Task<IReadOnlyList<Invoice>> ListAsync(string? status, DateTime? fromUtc, CancellationToken ct = default);
    Task<Invoice?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Invoice>> FindOpenForBookingAsync(Guid bookingId, CancellationToken ct = default);
    Task SaveAsync(Invoice invoice, CancellationToken ct = default);

    /// <summary>Next invoice number for the current tenant, committed with the caller's session.</summary>
    Task<long> NextInvoiceNumberAsync(CancellationToken ct = default);
}
