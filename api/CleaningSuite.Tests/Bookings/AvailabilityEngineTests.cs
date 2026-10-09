using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CleaningSuite.Application.Bookings;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Services;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Bookings;
using CleaningSuite.Domain.Services;
using CleaningSuite.Domain.Tenants;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Bookings;

public class AvailabilityEngineTests
{
    private readonly Mock<IServiceRepository> _servicesMock = new();
    private readonly Mock<IBookingRepository> _bookingsMock = new();
    private readonly Mock<ITenantProfileRepository> _profilesMock = new();

    private readonly AvailabilityEngine _engine;

    public AvailabilityEngineTests()
    {
        _engine = new AvailabilityEngine(_servicesMock.Object, _bookingsMock.Object, _profilesMock.Object);
    }

    [Fact]
    public async Task GetSlotsAsync_ReturnsAllSlots_WhenNoBookingsExist()
    {
        var serviceId = Guid.NewGuid();
        var service = new Service
        {
            Id = serviceId,
            DurationMinutes = 60,
            PriceNet = 50.0m,
            VatRatePercent = 25.5m,
            IsActive = true
        };

        _servicesMock.Setup(s => s.GetByIdAsync(serviceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(service);
        _profilesMock.Setup(p => p.GetAsync("test-tenant", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantProfile { TimeZoneId = "Europe/Helsinki" });
        _bookingsMock.Setup(b => b.ListForLocalDateAsync("2026-04-01", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Booking>());

        var slots = await _engine.GetSlotsAsync("test-tenant", "2026-04-01", serviceId, CancellationToken.None);

        Assert.NotEmpty(slots);
        // Window 08:00 to 20:00 (12 hours) with 30 min step for 60 min service => 23 slots
        Assert.Equal(23, slots.Count);
        Assert.Equal("08:00", slots[0].StartTime);
        Assert.Equal("09:00", slots[0].EndTime);
    }

    [Fact]
    public async Task GetSlotsAsync_ExcludesConflictingSlots_WhenBookingExists()
    {
        var serviceId = Guid.NewGuid();
        var service = new Service
        {
            Id = serviceId,
            DurationMinutes = 60,
            PriceNet = 50.0m,
            VatRatePercent = 25.5m,
            IsActive = true
        };

        // Date: 2026-04-01, Europe/Helsinki is UTC+3 (EEST)
        // 09:00 to 10:00 local is 06:00 UTC to 07:00 UTC
        var startUtc = new DateTime(2026, 4, 1, 6, 0, 0, DateTimeKind.Utc);
        var endUtc = new DateTime(2026, 4, 1, 7, 0, 0, DateTimeKind.Utc);

        var booking = new Booking
        {
            BookingNumber = "CS-1001",
            StartUtc = startUtc,
            EndUtc = endUtc,
            StartLocalDate = "2026-04-01",
            StartLocalTime = "09:00"
        };

        _servicesMock.Setup(s => s.GetByIdAsync(serviceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(service);
        _profilesMock.Setup(p => p.GetAsync("test-tenant", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantProfile { TimeZoneId = "Europe/Helsinki" });
        _bookingsMock.Setup(b => b.ListForLocalDateAsync("2026-04-01", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Booking> { booking });

        var slots = await _engine.GetSlotsAsync("test-tenant", "2026-04-01", serviceId, CancellationToken.None);

        // Slots that overlap with 09:00-10:00 should be excluded (08:30-09:30, 09:00-10:00, 09:30-10:30)
        Assert.DoesNotContain(slots, s => s.StartTime == "09:00");
        Assert.DoesNotContain(slots, s => s.StartTime == "08:30");
        Assert.DoesNotContain(slots, s => s.StartTime == "09:30");
        Assert.Contains(slots, s => s.StartTime == "08:00");
        Assert.Contains(slots, s => s.StartTime == "10:00");
    }

    [Fact]
    public async Task GetSlotsAsync_ThrowsNotFoundException_WhenServiceNotFound()
    {
        _servicesMock.Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Service?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _engine.GetSlotsAsync("test-tenant", "2026-04-01", Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task ReserveAsync_ThrowsSlotConflictException_WhenServiceInactive()
    {
        var service = new Service { Id = Guid.NewGuid(), DurationMinutes = 60, IsActive = false };

        _profilesMock.Setup(p => p.GetAsync("test-tenant", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantProfile { TimeZoneId = "Europe/Helsinki" });

        var futureDate = DateTime.UtcNow.AddDays(2).ToString("yyyy-MM-dd");

        await Assert.ThrowsAsync<SlotConflictException>(() =>
            _engine.ReserveAsync("test-tenant", futureDate, "10:00", service, null, CancellationToken.None));
    }
}
