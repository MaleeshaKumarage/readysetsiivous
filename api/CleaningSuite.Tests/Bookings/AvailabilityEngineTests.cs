using CleaningSuite.Application.Bookings;
using CleaningSuite.Application.Services;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Bookings;
using CleaningSuite.Domain.Services;
using CleaningSuite.Domain.Tenants;
using Moq;

namespace CleaningSuite.Tests.Bookings;

public class AvailabilityEngineTests
{
    private readonly Mock<IServiceRepository> _services = new();
    private readonly Mock<IBookingRepository> _bookings = new();
    private readonly Mock<ITenantProfileRepository> _profiles = new();
    private readonly Service _service = new() { DurationMinutes = 60 };

    private AvailabilityEngine Engine(int minHours = 2)
    {
        _services.Setup(x => x.GetByIdAsync(_service.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_service);
        _profiles.Setup(x => x.GetAsync("tenant", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantProfile { TimeZoneId = "Europe/Helsinki", MinHoursBeforeBooking = minHours });
        _bookings.Setup(x => x.ListForLocalDateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Booking>());
        _bookings.Setup(x => x.FindOverlappingAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Booking>());
        // 10:00 local time in Helsinki; the earliest booking with two hours' notice is 12:00.
        return new AvailabilityEngine(_services.Object, _bookings.Object, _profiles.Object,
            new FixedTimeProvider(new DateTimeOffset(2026, 10, 13, 7, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task An_unknown_service_is_refused_with_a_next_step_and_without_the_internal_id()
    {
        var engine = Engine();
        var unknown = Guid.NewGuid();
        _services.Setup(x => x.GetByIdAsync(unknown, It.IsAny<CancellationToken>())).ReturnsAsync((Service?)null);
        var ex = await Assert.ThrowsAsync<CleaningSuite.Application.Common.PublicServiceNotOfferedException>(() => engine.GetSlotsAsync("tenant", "2026-10-13", unknown, default));
        Assert.Equal("That service is no longer offered. Choose another service, then try again.", ex.Message);
        Assert.DoesNotContain(unknown.ToString(), ex.Message);
    }

    [Theory]
    [InlineData(0, "10:00")]
    [InlineData(2, "12:00")]
    [InlineData(4, "14:00")]
    public async Task Slots_and_reservation_apply_the_same_notice_period(int minHours, string firstTime)
    {
        var engine = Engine(minHours);
        var slots = await engine.GetSlotsAsync("tenant", "2026-10-13", _service.Id, default);
        Assert.Equal(firstTime, slots[0].StartTime);
        foreach (var slot in slots)
            await engine.ReserveAsync("tenant", "2026-10-13", slot.StartTime, _service);
        var earlierTime = TimeOnly.Parse(firstTime).AddMinutes(-30).ToString("HH:mm");
        await Assert.ThrowsAsync<SlotConflictException>(() =>
            engine.ReserveAsync("tenant", "2026-10-13", earlierTime, _service));
    }

    [Fact]
    public async Task Past_dates_have_no_slots()
    {
        var engine = Engine();
        Assert.Empty(await engine.GetSlotsAsync("tenant", "2026-10-12", _service.Id, default));
    }

    [Fact]
    public async Task Notice_filter_preserves_overlap_and_business_hour_checks()
    {
        var engine = Engine();
        _bookings.Setup(x => x.ListForLocalDateAsync("2026-10-13", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new Booking {
                StartUtc = new DateTime(2026, 10, 13, 9, 0, 0, DateTimeKind.Utc),
                EndUtc = new DateTime(2026, 10, 13, 10, 0, 0, DateTimeKind.Utc)
            } });
        var slots = await engine.GetSlotsAsync("tenant", "2026-10-13", _service.Id, default);
        Assert.Equal("13:00", slots[0].StartTime);
        Assert.Equal("19:00", slots[^1].StartTime);
    }

    [Fact]
    public async Task Inactive_services_have_no_slots()
    {
        var engine = Engine();
        _service.IsActive = false;
        Assert.Empty(await engine.GetSlotsAsync("tenant", "2026-10-14", _service.Id, default));
    }

    [Theory]
    [InlineData("Europe/Helsinki", "2027-03-28", 3)]
    [InlineData("Europe/Helsinki", "2027-10-31", 2)]
    [InlineData("UTC", "2027-03-28", 0)]
    [InlineData("", "2027-03-28", 3)]
    public async Task Repeated_queries_preserve_timezone_and_touching_booking_boundaries(string timezone, string date, int offset)
    {
        var engine = Engine();
        _profiles.Setup(x => x.GetAsync("tenant", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantProfile { TimeZoneId = timezone, MinHoursBeforeBooking = 2 });
        var day = DateTime.SpecifyKind(DateOnly.ParseExact(date, "yyyy-MM-dd").ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        // Deliberately unsorted bookings; 09:00-10:00 is unavailable, with both touching slots available.
        _bookings.Setup(x => x.ListForLocalDateAsync(date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] {
                new Booking { StartUtc = day.AddHours(18-offset), EndUtc = day.AddHours(19-offset) },
                new Booking { StartUtc = day.AddHours(9-offset), EndUtc = day.AddHours(10-offset) }
            });
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var slots = await engine.GetSlotsAsync("tenant", date, _service.Id, default);
            Assert.Equal(17, slots.Count);
            Assert.Contains(slots, x => x.StartTime == "08:00" && x.EndTime == "09:00");
            Assert.Contains(slots, x => x.StartTime == "10:00");
            Assert.Contains(slots, x => x.StartTime == "19:00" && x.EndTime == "20:00");
            Assert.DoesNotContain(slots, x => x.StartTime is "08:30" or "09:00" or "09:30" or "17:30" or "18:00" or "18:30");
        }
    }

    [Theory]
    [InlineData(30, 24)]
    [InlineData(60, 23)]
    [InlineData(720, 1)]
    [InlineData(750, 0)]
    public async Task Duration_respects_business_window(int duration, int count)
    {
        var engine = Engine();
        _service.DurationMinutes = duration;
        var slots = await engine.GetSlotsAsync("tenant", "2027-01-15", _service.Id, default);
        Assert.Equal(count, slots.Count);
        Assert.All(slots, slot => Assert.True(TimeOnly.Parse(slot.EndTime) <= new TimeOnly(20, 0)));
    }

    [Fact]
    public async Task A_refused_slot_says_what_to_choose_instead()
    {
        var engine = Engine();
        var pastDay = await Assert.ThrowsAsync<SlotConflictException>(() => engine.ReserveAsync("tenant", "2020-01-01", "09:00", _service));
        Assert.Contains("Choose a later date or time", pastDay.Message);
        var closed = await Assert.ThrowsAsync<SlotConflictException>(() => engine.ReserveAsync("tenant", "2027-01-15", "03:17", _service));
        Assert.Contains("opening hours", closed.Message);
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;

}
