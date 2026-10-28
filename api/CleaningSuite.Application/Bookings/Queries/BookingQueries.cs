using CleaningSuite.Application.Bookings;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Bookings;
using MediatR;

namespace CleaningSuite.Application.Bookings.Queries;

public record GetAvailabilityQuery(string LocalDate, Guid ServiceId) : IRequest<IReadOnlyList<AvailabilitySlot>>;

public class GetAvailabilityHandler : IRequestHandler<GetAvailabilityQuery, IReadOnlyList<AvailabilitySlot>>
{
    private readonly ITenantContext _context;
    private readonly AvailabilityEngine _availability;

    public GetAvailabilityHandler(ITenantContext context, AvailabilityEngine availability)
    {
        _context = context;
        _availability = availability;
    }

    public Task<IReadOnlyList<AvailabilitySlot>> Handle(GetAvailabilityQuery request, CancellationToken ct) =>
        _availability.GetSlotsAsync(_context.TenantId, request.LocalDate, request.ServiceId, ct);
}

public record GetPublicBookingQuery(string BookingNumber, string? ManagementKey) : IRequest<PublicBookingDto?>
{
    public override string ToString() => nameof(GetPublicBookingQuery);
}

public record PublicBookingDto(
    string BookingNumber,
    string Status,
    string StartLocalDate,
    string StartLocalTime,
    string ServiceNameFi,
    string CustomerName,
    string ServiceNameEn,
    string ServiceNameSv,
    int DurationMinutes,
    decimal PriceGross,
    string Currency);

public class GetPublicBookingHandler : IRequestHandler<GetPublicBookingQuery, PublicBookingDto?>
{
    private readonly IBookingRepository _bookings;

    public GetPublicBookingHandler(IBookingRepository bookings) => _bookings = bookings;

    public async Task<PublicBookingDto?> Handle(GetPublicBookingQuery request, CancellationToken ct)
    {
        var booking = await _bookings.GetByNumberAsync(request.BookingNumber, ct);
        if (booking is null
            || !BookingManagementKey.Verify(request.ManagementKey, booking.ManagementKeyHash))
            return null;

        return new PublicBookingDto(
            booking.BookingNumber,
            booking.Status,
            booking.StartLocalDate,
            booking.StartLocalTime,
            booking.Service.NameFi,
            booking.Customer.Name,
            booking.Service.NameEn,
            booking.Service.NameSv,
            booking.Service.DurationMinutes,
            booking.Total.Gross,
            booking.Service.Currency);
    }
}

public record ListBookingsQuery(
    DateTime? FromUtc,
    DateTime? ToUtc,
    string? Status,
    Guid? EmployeeId) : IRequest<IReadOnlyList<Booking>>;

public class ListBookingsHandler : IRequestHandler<ListBookingsQuery, IReadOnlyList<Booking>>
{
    private readonly IBookingRepository _bookings;

    public ListBookingsHandler(IBookingRepository bookings) => _bookings = bookings;

    public Task<IReadOnlyList<Booking>> Handle(ListBookingsQuery request, CancellationToken ct) =>
        _bookings.ListAsync(request.FromUtc, request.ToUtc, request.Status, request.EmployeeId, ct);
}

public record GetBookingDetailQuery(Guid Id) : IRequest<Booking?>;

public class GetBookingDetailHandler : IRequestHandler<GetBookingDetailQuery, Booking?>
{
    private readonly IBookingRepository _bookings;

    public GetBookingDetailHandler(IBookingRepository bookings) => _bookings = bookings;

    public Task<Booking?> Handle(GetBookingDetailQuery request, CancellationToken ct) =>
        _bookings.GetByIdAsync(request.Id, ct);
}
