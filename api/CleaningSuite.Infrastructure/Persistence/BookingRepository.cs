using CleaningSuite.Application.Bookings;
using CleaningSuite.Domain.Bookings;
using CleaningSuite.Domain.Common;
using CleaningSuite.Infrastructure.Persistence;
using Marten;

namespace CleaningSuite.Infrastructure.Persistence;

/// <summary>
/// Bookings live in the request tenant's partition. Counter increments happen
/// in the same session as the booking insert, so the number sequence is atomic
/// with the create; on concurrency conflict the caller retries.
/// </summary>
public class BookingRepository : IBookingRepository
{
    private readonly ITenantSession _tenantSession;

    public BookingRepository(ITenantSession tenantSession) => _tenantSession = tenantSession;

    public async Task<IReadOnlyList<Booking>> FindOverlappingAsync(
        DateTime startUtc, DateTime endUtc, Guid? exceptId = null, CancellationToken ct = default)
    {
        // Npgsql 8 rejects Kind=Utc params against timestamp-without-tz expressions.
        var start = DateTime.SpecifyKind(startUtc, DateTimeKind.Unspecified);
        var end = DateTime.SpecifyKind(endUtc, DateTimeKind.Unspecified);

        var results = await _tenantSession.Session.Query<Booking>()
            .Where(b => b.StartUtc < end && b.EndUtc > start
                && b.Status != Booking.StatusCancelled
                && (exceptId == null || b.Id != exceptId))
            .ToListAsync(ct);
        return results;
    }

    public async Task<IReadOnlyList<Booking>> ListForLocalDateAsync(
        string localDate, CancellationToken ct = default)
    {
        var results = await _tenantSession.Session.Query<Booking>()
            .Where(b => b.StartLocalDate == localDate && b.Status != Booking.StatusCancelled)
            .ToListAsync(ct);
        return results;
    }

    public Task<Booking?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _tenantSession.Session.LoadAsync<Booking>(id, ct);

    public async Task<Booking?> GetByNumberAsync(string bookingNumber, CancellationToken ct = default)
    {
        var results = await _tenantSession.Session.Query<Booking>()
            .Where(b => b.BookingNumber == bookingNumber).ToListAsync(ct);
        return results.FirstOrDefault();
    }

    public async Task SaveAsync(Booking booking, CancellationToken ct = default)
    {
        _tenantSession.Session.Store(booking);
        await _tenantSession.Session.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<Booking>> ListAsync(
        DateTime? fromUtc, DateTime? toUtc, string? status, Guid? employeeId, CancellationToken ct = default)
    {
        IQueryable<Booking> query = _tenantSession.Session.Query<Booking>();
        if (fromUtc.HasValue)
        {
            var from = DateTime.SpecifyKind(fromUtc.Value, DateTimeKind.Unspecified);
            query = query.Where(b => b.StartUtc >= from);
        }
        if (toUtc.HasValue)
        {
            var to = DateTime.SpecifyKind(toUtc.Value, DateTimeKind.Unspecified);
            query = query.Where(b => b.StartUtc < to);
        }
        if (!string.IsNullOrEmpty(status))
            query = query.Where(b => b.Status == status);
        if (employeeId.HasValue)
            query = query.Where(b => b.EmployeeId == employeeId.Value.ToString());

        return await query.OrderBy(b => b.StartUtc).ToListAsync(ct);
    }

    /// <summary>
    /// Next value for a per-tenant sequence. Runs inside the request's session
    /// so the increment commits atomically with the booking insert.
    /// </summary>
    public async Task<long> NextCounterAsync(string name, CancellationToken ct = default)
    {
        var id = TenantDocumentIds.Counter(_tenantSession.TenantId, name);
        var counter = await _tenantSession.Session.LoadAsync<Counter>(id, ct)
            ?? new Counter { Id = id, Name = name, Value = 0 };

        counter.Value++;
        _tenantSession.Session.Store(counter);
        return counter.Value;
    }
}
