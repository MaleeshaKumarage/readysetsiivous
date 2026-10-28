using CleaningSuite.Application.Bookings;
using CleaningSuite.Application.Bookings.Commands;
using CleaningSuite.Application.Services;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Bookings;
using CleaningSuite.Domain.Services;
using CleaningSuite.Domain.Tenants;
using Moq;
using Npgsql;

namespace CleaningSuite.Tests.Bookings;

public class CreatePublicBookingHandlerTests
{
    private readonly Mock<IBookingRepository> _bookings = new();
    private readonly Mock<IServiceRepository> _services = new();
    private readonly Mock<ITenantProfileRepository> _profiles = new();
    private readonly Mock<ITenantContext> _context = new();
    private readonly Service _service = new() { DurationMinutes = 60, PriceNet = 50 };

    private CreatePublicBookingHandler Handler()
    {
        _context.SetupGet(x => x.TenantId).Returns("tenant");
        _services.Setup(x => x.GetByIdAsync(_service.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_service);
        _profiles.Setup(x => x.GetAsync("tenant", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantProfile { TimeZoneId = "Europe/Helsinki" });
        _bookings.Setup(x => x.FindOverlappingAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Booking>());
        _bookings.Setup(x => x.NextCounterAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var engine = new AvailabilityEngine(_services.Object, _bookings.Object, _profiles.Object,
            new FixedTimeProvider(new DateTimeOffset(2026, 10, 13, 7, 0, 0, TimeSpan.Zero)));
        return new CreatePublicBookingHandler(_context.Object, _services.Object, _bookings.Object, engine);
    }

    private CreatePublicBookingCommand Request() => new("2026-10-14", "12:00", _service.Id,
        "Customer", "123", "customer@example.com", "Street", "00100", "Helsinki", null);

    private static Exception SerializationFailure() =>
        new PostgresException("Concurrent reservation", "ERROR", "ERROR", PostgresErrorCodes.SerializationFailure);

    [Fact]
    public async Task Changed_price_reads_snapshot_once_but_never_reserves_slot_or_saves()
    {
        _service.VatRatePercent = 25.5m;
        var handler = Handler();
        await Assert.ThrowsAsync<SlotConflictException>(() => handler.Handle(Request() with { ExpectedPriceGross = 50m }, default));
        _bookings.Verify(x => x.SaveAsync(It.IsAny<Booking>(), It.IsAny<CancellationToken>()), Times.Never);
        _bookings.Verify(x => x.NextCounterAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _bookings.Verify(x => x.BeginReservationAsync(It.IsAny<CancellationToken>()), Times.Once);
        _services.Verify(x => x.GetByIdAsync(_service.Id, It.IsAny<CancellationToken>()), Times.Once);
        _bookings.Verify(x => x.FindOverlappingAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), null, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Accepted_price_returns_server_calculated_vat_inclusive_amount()
    {
        _service.VatRatePercent = 25.5m;
        var result = await Handler().Handle(Request() with { ExpectedPriceGross = 62.75m }, default);
        Assert.Equal(62.75m, result.PriceGross);
        Assert.Equal("EUR", result.Currency);
    }

    [Fact]
    public async Task Creation_returns_raw_key_once_and_persists_only_its_digest()
    {
        var handler = Handler();
        Booking? saved = null;
        _bookings.Setup(x => x.SaveAsync(It.IsAny<Booking>(), It.IsAny<CancellationToken>()))
            .Callback<Booking, CancellationToken>((booking, _) => saved = booking).Returns(Task.CompletedTask);
        var result = await handler.Handle(Request(), default);
        Assert.NotNull(saved);
        Assert.True(BookingManagementKey.Verify(result.ManagementKey, saved.ManagementKeyHash));
        Assert.DoesNotContain(result.ManagementKey, System.Text.Json.JsonSerializer.Serialize(saved));
        Assert.Equal(saved.BookingNumber, result.BookingNumber);
    }

    [Fact]
    public async Task Conflict_restarts_transaction_and_reloads_service_slot_and_counter()
    {
        var handler = Handler();
        _bookings.SetupSequence(x => x.SaveAsync(It.IsAny<Booking>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database error", SerializationFailure()))
            .Returns(Task.CompletedTask);
        _bookings.SetupSequence(x => x.NextCounterAsync("booking-2026", It.IsAny<CancellationToken>()))
            .ReturnsAsync(1).ReturnsAsync(2);

        var result = await handler.Handle(Request(), default);

        Assert.Equal("RSS-2026-000002", result.BookingNumber);
        _bookings.Verify(x => x.BeginReservationAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        _services.Verify(x => x.GetByIdAsync(_service.Id, It.IsAny<CancellationToken>()), Times.Exactly(2));
        _bookings.Verify(x => x.FindOverlappingAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), null, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Slot_taken_after_conflict_is_rejected_instead_of_saved_again()
    {
        var handler = Handler();
        _bookings.Setup(x => x.SaveAsync(It.IsAny<Booking>(), It.IsAny<CancellationToken>())).ThrowsAsync(SerializationFailure());
        _bookings.SetupSequence(x => x.FindOverlappingAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Booking>()).ReturnsAsync(new[] { new Booking() });

        await Assert.ThrowsAsync<SlotConflictException>(() => handler.Handle(Request(), default));

        _bookings.Verify(x => x.BeginReservationAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        _bookings.Verify(x => x.SaveAsync(It.IsAny<Booking>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Repeated_conflicts_are_bounded_and_return_slot_conflict()
    {
        var handler = Handler();
        _bookings.Setup(x => x.SaveAsync(It.IsAny<Booking>(), It.IsAny<CancellationToken>())).ThrowsAsync(SerializationFailure());

        await Assert.ThrowsAsync<SlotConflictException>(() => handler.Handle(Request(), default));

        _bookings.Verify(x => x.BeginReservationAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Non_concurrency_errors_are_not_retried()
    {
        var handler = Handler();
        _bookings.Setup(x => x.SaveAsync(It.IsAny<Booking>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("Disk error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(Request(), default));
        _bookings.Verify(x => x.BeginReservationAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
