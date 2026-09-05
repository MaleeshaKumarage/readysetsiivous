using CleaningSuite.Domain.Bookings;

namespace CleaningSuite.Application.Bookings;

public interface IBookingRepository
{
    Task<IReadOnlyList<Booking>> FindOverlappingAsync(
        DateTime startUtc, DateTime endUtc, Guid? exceptId = null, CancellationToken ct = default);

    Task<IReadOnlyList<Booking>> ListForLocalDateAsync(
        string localDate, CancellationToken ct = default);

    Task<Booking?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Booking?> GetByNumberAsync(string bookingNumber, CancellationToken ct = default);
    Task SaveAsync(Booking booking, CancellationToken ct = default);
    Task<IReadOnlyList<Booking>> ListAsync(
        DateTime? fromUtc, DateTime? toUtc, string? status, Guid? employeeId, CancellationToken ct = default);

    /// <summary>Next value for a per-tenant sequence, committed with the caller's session.</summary>
    Task<long> NextCounterAsync(string name, CancellationToken ct = default);
}
