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
using Npgsql;

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
    string? Notes,
    decimal? ExpectedPriceGross = null) : IRequest<PublicBookingResult>;

public record PublicBookingResult(
    Guid BookingId,
    string BookingNumber,
    string CustomerReference,
    string ManagementKey,
    decimal PriceGross = 0,
    string Currency = "EUR")
{
    public override string ToString() => nameof(PublicBookingResult);
}

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
        RuleFor(x => x.CustomerEmail).MaximumLength(254);
        RuleFor(x => x.PostalCode).MaximumLength(20);
        RuleFor(x => x.Notes).MaximumLength(2000);
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
        // All reads and writes share a serializable transaction. A retry must
        // discard tracked documents and recheck availability against committed bookings.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await _bookings.BeginReservationAsync(ct);
                var service = await _services.GetByIdAsync(request.ServiceId, ct)
                    ?? throw new NotFoundException("Service", request.ServiceId);
                if (request.ExpectedPriceGross is { } expected && expected != Money.FromNet(service.PriceNet, service.VatRatePercent).Gross)
                    throw new SlotConflictException("Service price changed. Review the current service price before submitting again.");
                var (startUtc, endUtc) = await _availability.ReserveAsync(
                    _context.TenantId, request.LocalDate, request.StartTime, service, ct: ct);
                var number = await _bookings.NextCounterAsync($"booking-{startUtc.Year}", ct);

                var managementKey = BookingManagementKey.Generate();
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
                        Currency = service.Currency,
                    },
                    StartUtc = startUtc,
                    EndUtc = endUtc,
                    StartLocalDate = request.LocalDate,
                    StartLocalTime = request.StartTime,
                    Total = Money.FromNet(service.PriceNet, service.VatRatePercent),
                    Status = Booking.StatusNew,
                    Source = "Public",
                    CustomerReference = GenerateReference(),
                    ManagementKeyHash = BookingManagementKey.Hash(managementKey),
                    Notes = request.Notes,
                    History = [new StatusEvent { Status = Booking.StatusNew, By = "customer" }],
                };

                await _bookings.SaveAsync(booking, ct);
                return new PublicBookingResult(booking.Id, booking.BookingNumber, booking.CustomerReference!, managementKey, booking.Total.Gross, service.Currency);
            }
            catch (Exception ex) when (IsReservationConflict(ex))
            {
                if (attempt == 2)
                    throw new SlotConflictException("Booking changed concurrently, please try again");
            }
        }

        throw new SlotConflictException("Could not allocate booking number, try again");
    }

    private static bool IsReservationConflict(Exception exception) =>
        exception is ConcurrencyException
        || exception is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure }
        || (exception.InnerException is { } inner && IsReservationConflict(inner));

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

public record CancelPublicBookingCommand(string BookingNumber, string? ManagementKey) : IRequest<Unit>
{
    public override string ToString() => nameof(CancelPublicBookingCommand);
}

public class CancelPublicBookingHandler : IRequestHandler<CancelPublicBookingCommand, Unit>
{
    private readonly IBookingRepository _bookings;

    public CancelPublicBookingHandler(IBookingRepository bookings) => _bookings = bookings;

    public async Task<Unit> Handle(CancelPublicBookingCommand request, CancellationToken ct)
    {
        var booking = await _bookings.GetByNumberAsync(request.BookingNumber, ct);
        if (booking is null || !BookingManagementKey.Verify(request.ManagementKey, booking.ManagementKeyHash))
            throw new NotFoundException("Booking", Guid.Empty);

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
