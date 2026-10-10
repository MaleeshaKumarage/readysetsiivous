using CleaningSuite.Application.Bookings;
using CleaningSuite.Application.Services;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Bookings;
using CleaningSuite.Domain.Common;
using CleaningSuite.Domain.Services;
using CleaningSuite.Domain.Tenants;
using NSubstitute;

namespace CleaningSuite.Tests.Bookings;

public class AvailabilityEngineTests
{
    private readonly IServiceRepository _services = Substitute.For<IServiceRepository>();
    private readonly IBookingRepository _bookings = Substitute.For<IBookingRepository>();
    private readonly ITenantProfileRepository _profiles = Substitute.For<ITenantProfileRepository>();
    private readonly AvailabilityEngine _engine;

    public AvailabilityEngineTests()
    {
        _engine = new AvailabilityEngine(_services, _bookings, _profiles);
    }

    [Fact]
    public async Task GetSlotsAsync_ReturnsAllSlots_WhenNoOccupiedBookings()
    {
        var serviceId = Guid.NewGuid();
        _services.GetByIdAsync(serviceId, Arg.Any<CancellationToken>())
            .Returns(new Service { Id = serviceId, Name = new LocalizedText { Values = { ["fi"] = "Cleaning" } }, DurationMinutes = 60, IsActive = true });

        _profiles.GetAsync("test-tenant", Arg.Any<CancellationToken>())
            .Returns(new TenantProfile { Slug = "test-tenant", TimeZoneId = "UTC" });

        _bookings.ListForLocalDateAsync("2026-05-10", Arg.Any<CancellationToken>())
            .Returns(new List<Booking>());

        var slots = await _engine.GetSlotsAsync("test-tenant", "2026-05-10", serviceId, CancellationToken.None);

        Assert.NotEmpty(slots);
        // Window is 08:00 to 20:00 (12 hours). With 60 min duration and 30 min step, last slot starts at 19:00.
        Assert.Equal(23, slots.Count);
        Assert.Equal("08:00", slots[0].StartTime);
        Assert.Equal("09:00", slots[0].EndTime);
        Assert.Equal("19:00", slots[^1].StartTime);
        Assert.Equal("20:00", slots[^1].EndTime);
    }

    [Fact]
    public async Task GetSlotsAsync_FiltersOutConflictingSlots_WhenBookingExists()
    {
        var serviceId = Guid.NewGuid();
        _services.GetByIdAsync(serviceId, Arg.Any<CancellationToken>())
            .Returns(new Service { Id = serviceId, Name = new LocalizedText { Values = { ["fi"] = "Cleaning" } }, DurationMinutes = 60, IsActive = true });

        _profiles.GetAsync("test-tenant", Arg.Any<CancellationToken>())
            .Returns(new TenantProfile { Slug = "test-tenant", TimeZoneId = "UTC" });

        // Existing booking from 09:00 UTC to 10:00 UTC on 2026-05-10
        var occupiedBooking = new Booking
        {
            Id = Guid.NewGuid(),
            StartUtc = new DateTime(2026, 5, 10, 9, 0, 0, DateTimeKind.Utc),
            EndUtc = new DateTime(2026, 5, 10, 10, 0, 0, DateTimeKind.Utc),
            Status = Booking.StatusConfirmed
        };

        _bookings.ListForLocalDateAsync("2026-05-10", Arg.Any<CancellationToken>())
            .Returns(new List<Booking> { occupiedBooking });

        var slots = await _engine.GetSlotsAsync("test-tenant", "2026-05-10", serviceId, CancellationToken.None);

        // Slots overlapping with 09:00-10:00:
        // 08:30-09:30 overlaps (08:30 < 10:00 && 09:00 < 09:30)
        // 09:00-10:00 overlaps
        // 09:30-10:30 overlaps (09:30 < 10:00 && 09:00 < 10:30)
        // Non-overlapping slots before: 08:00-09:00 (ends exactly at 09:00)
        // Non-overlapping slots after: 10:00-11:00 (starts exactly at 10:00)
        Assert.DoesNotContain(slots, s => s.StartTime == "08:30");
        Assert.DoesNotContain(slots, s => s.StartTime == "09:00");
        Assert.DoesNotContain(slots, s => s.StartTime == "09:30");
        Assert.Contains(slots, s => s.StartTime == "08:00");
        Assert.Contains(slots, s => s.StartTime == "10:00");
    }
}
