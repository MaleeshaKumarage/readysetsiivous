using System.Security.Cryptography;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Services;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Bookings;
using CleaningSuite.Domain.Common;
using CleaningSuite.Domain.Services;
using FluentValidation;
using JasperFx;
using MediatR;

namespace CleaningSuite.Application.Bookings.Commands;

// ---- public booking ----

public record CreatePublicBookingCommand(
    string LocalDate,
    string StartTime,
    Guid ServiceId,
    string CustomerName,
    string CustomerPhone,
    string CustomerEmail,
    string Street,
    string PostalCode,
    string City,
    string? Notes) : IRequest<PublicBookingResult>;

public record PublicBookingResult(
    Guid BookingId,
    string BookingNumber,
    string CustomerReference);

public class CreatePublicBookingValidator : AbstractValidator<CreatePublicBookingCommand>
{
    public CreatePublicBookingValidator()
    {
        RuleFor(x => x.LocalDate).Matches(@"^\d{4}-\d{2}-\d{2}$");
        RuleFor(x => x.StartTime).Matches(@"^\d{2}:\d{2}$");
        RuleFor(x => x.CustomerName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.CustomerPhone).NotEmpty().MaximumLength(40);
        RuleFor(x => x.CustomerEmail).EmailAddress().When(x => !string.IsNullOrEmpty(x.CustomerEmail));
        RuleFor(x => x.Street).NotEmpty().MaximumLength(200);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
    }
}

public class CreatePublicBookingHandler : IRequestHandler<CreatePublicBookingCommand, PublicBookingResult>
{
    private readonly ITenantContext _context;
    private readonly IServiceRepository _services;
    private readonly IBookingRepository _bookings;
    private readonly AvailabilityEngine _availability;

    public CreatePublicBookingHandler(
        ITenantContext context,
        IServiceRepository services,
        IBookingRepository bookings,
        AvailabilityEngine availability)
    {
        _context = context;
        _services = services;
        _bookings = bookings;
        _availability = availability;
    }

    public async Task<PublicBookingResult> Handle(CreatePublicBookingCommand request, CancellationToken ct)
    {
        var service = await _services.GetByIdAsync(request.ServiceId, ct)
            ?? throw new NotFoundException("Service", request.ServiceId);

        var (startUtc, endUtc) = await _availability.ReserveAsync(
            _context.TenantId, request.LocalDate, request.StartTime, service, ct: ct);

        // Counter increment shares the request session with the insert; retry the
        // whole transaction on optimistic conflict (counter is the hot row).
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var number = await _bookings.NextCounterAsync($"booking-{startUtc.Year}", ct);

                var booking = new Booking
                {
                    BookingNumber = $"RSS-{startUtc.Year}-{number:D6}",
                    Customer = new Customer
                    {
                        Name = request.CustomerName,
                        Phone = request.CustomerPhone,
                        Email = request.CustomerEmail,
                    },
                    CleaningAddress = new Address
                    {
                        Street = request.Street,
                        PostalCode = request.PostalCode,
                        City = request.City,
                    },
                    ServiceId = service.Id.ToString(),
                    Service = new ServiceSnapshot
                    {
                        NameFi = service.Name.For("fi") ?? "",
                        NameEn = service.Name.For("en") ?? "",
                        NameSv = service.Name.For("sv") ?? "",
                        DurationMinutes = service.DurationMinutes,
                        PriceNet = service.PriceNet,
                        VatRatePercent = service.VatRatePercent,
                    },
                    StartUtc = startUtc,
                    EndUtc = endUtc,
                    StartLocalDate = request.LocalDate,
                    StartLocalTime = request.StartTime,
                    Total = Money.FromNet(service.PriceNet, service.VatRatePercent),
                    Status = Booking.StatusNew,
                    Source = "Public",
                    CustomerReference = GenerateReference(),
                    Notes = request.Notes,
                    History = [new StatusEvent { Status = Booking.StatusNew, By = "customer" }],
                };

                await _bookings.SaveAsync(booking, ct);
                return new PublicBookingResult(booking.Id, booking.BookingNumber, booking.CustomerReference!);
            }
            catch (ConcurrencyException) when (attempt < 2)
            {
                // counter raced; retry with fresh session state
            }
        }

        throw new SlotConflictException("Could not allocate booking number, try again");
    }

    private static string GenerateReference()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return string.Create(6, RandomNumberGenerator.GetInt32(int.MaxValue), (chars, seed) =>
        {
            for (var i = 0; i < chars.Length; i++)
                chars[i] = alphabet[(seed >> (i * 5)) % alphabet.Length];
        });
    }
}

// ---- public cancel ----

public record CancelPublicBookingCommand(string BookingNumber, string Phone) : IRequest<Unit>;

public class CancelPublicBookingValidator : AbstractValidator<CancelPublicBookingCommand>
{
    public CancelPublicBookingValidator()
    {
        RuleFor(x => x.BookingNumber).NotEmpty();
        RuleFor(x => x.Phone).NotEmpty();
    }
}

public class CancelPublicBookingHandler : IRequestHandler<CancelPublicBookingCommand, Unit>
{
    private readonly IBookingRepository _bookings;

    public CancelPublicBookingHandler(IBookingRepository bookings) => _bookings = bookings;

    public async Task<Unit> Handle(CancelPublicBookingCommand request, CancellationToken ct)
    {
        var booking = await _bookings.GetByNumberAsync(request.BookingNumber, ct)
            ?? throw new NotFoundException("Booking", Guid.Empty);

        if (!string.Equals(booking.Customer.Phone.Trim(), request.Phone.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Phone does not match booking");

        if (booking.Status is Booking.StatusCancelled or Booking.StatusCompleted)
            throw new SlotConflictException("Booking can no longer be cancelled");

        booking.Status = Booking.StatusCancelled;
        booking.UpdatedUtc = DateTime.UtcNow;
        booking.History.Add(new StatusEvent { Status = Booking.StatusCancelled, By = "customer" });

        await _bookings.SaveAsync(booking, ct);
        return Unit.Value;
    }
}

// ---- admin actions ----

public record ConfirmBookingCommand(Guid Id, string? Note) : IRequest<Unit>;
public record CancelBookingCommand(Guid Id, string? Note) : IRequest<Unit>;
public record CompleteBookingCommand(Guid Id, string? Note) : IRequest<Unit>;

public abstract class StatusChangeHandler<T> : IRequestHandler<T, Unit> where T : class, IRequest<Unit>
{
    private readonly IBookingRepository _bookings;
    private readonly string _targetStatus;
    private readonly string _actor;

    protected StatusChangeHandler(IBookingRepository bookings, string targetStatus, string actor)
    {
        _bookings = bookings;
        _targetStatus = targetStatus;
        _actor = actor;
    }

    protected abstract Guid GetId(T request);
    protected abstract string? GetNote(T request);

    public async Task<Unit> Handle(T request, CancellationToken ct)
    {
        var booking = await _bookings.GetByIdAsync(GetId(request), ct)
            ?? throw new NotFoundException("Booking", GetId(request));

        if (booking.Status == Booking.StatusCancelled)
            throw new SlotConflictException("Booking is cancelled");

        booking.Status = _targetStatus;
        booking.UpdatedUtc = DateTime.UtcNow;
        booking.History.Add(new StatusEvent { Status = _targetStatus, By = _actor, Note = GetNote(request) });

        await _bookings.SaveAsync(booking, ct);
        return Unit.Value;
    }
}

public class ConfirmBookingHandler : StatusChangeHandler<ConfirmBookingCommand>
{
    public ConfirmBookingHandler(IBookingRepository bookings)
        : base(bookings, Booking.StatusConfirmed, "admin") { }

    protected override Guid GetId(ConfirmBookingCommand request) => request.Id;
    protected override string? GetNote(ConfirmBookingCommand request) => request.Note;
}

public class CancelBookingHandler : StatusChangeHandler<CancelBookingCommand>
{
    public CancelBookingHandler(IBookingRepository bookings)
        : base(bookings, Booking.StatusCancelled, "admin") { }

    protected override Guid GetId(CancelBookingCommand request) => request.Id;
    protected override string? GetNote(CancelBookingCommand request) => request.Note;
}

public class CompleteBookingHandler : StatusChangeHandler<CompleteBookingCommand>
{
    public CompleteBookingHandler(IBookingRepository bookings)
        : base(bookings, Booking.StatusCompleted, "admin") { }

    protected override Guid GetId(CompleteBookingCommand request) => request.Id;
    protected override string? GetNote(CompleteBookingCommand request) => request.Note;
}
