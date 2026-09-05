using CleaningSuite.Application.Invoicing;
using CleaningSuite.Domain.Common;
using CleaningSuite.Domain.Invoicing;
using CleaningSuite.Infrastructure.Persistence;
using Marten;

namespace CleaningSuite.Infrastructure.Persistence;

public class InvoiceRepository : IInvoiceRepository
{
    private readonly ITenantSession _tenantSession;

    public InvoiceRepository(ITenantSession tenantSession) => _tenantSession = tenantSession;

    public async Task<IReadOnlyList<Invoice>> ListAsync(
        string? status, DateTime? fromUtc, CancellationToken ct = default)
    {
        IQueryable<Invoice> query = _tenantSession.Session.Query<Invoice>();
        if (!string.IsNullOrEmpty(status))
            query = query.Where(i => i.Status == status);
        if (fromUtc.HasValue)
            query = query.Where(i => i.IssueDate >= fromUtc.Value);

        return await query.OrderByDescending(i => i.InvoiceNumber).ToListAsync(ct);
    }

    public Task<Invoice?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _tenantSession.Session.LoadAsync<Invoice>(id, ct);

    public async Task<IReadOnlyList<Invoice>> FindOpenForBookingAsync(Guid bookingId, CancellationToken ct = default)
    {
        var results = await _tenantSession.Session.Query<Invoice>()
            .Where(i => i.BookingId == bookingId && i.Status != Invoice.StatusVoided)
            .ToListAsync(ct);
        return results;
    }

    public async Task SaveAsync(Invoice invoice, CancellationToken ct = default)
    {
        _tenantSession.Session.Store(invoice);
        await _tenantSession.Session.SaveChangesAsync(ct);
    }

    public async Task<long> NextInvoiceNumberAsync(CancellationToken ct = default)
    {
        var year = DateTime.UtcNow.Year;
        var id = TenantDocumentIds.Counter(_tenantSession.TenantId, $"invoice-{year}");
        var counter = await _tenantSession.Session.LoadAsync<Counter>(id, ct)
            ?? new Counter { Id = id, Name = $"invoice-{year}", Value = 0 };

        counter.Value++;
        _tenantSession.Session.Store(counter);
        return counter.Value;
    }
}
