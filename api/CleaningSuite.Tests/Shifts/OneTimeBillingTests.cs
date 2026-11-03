using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Invoicing;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Invoicing;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Domain.Tenants;
using FluentValidation;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Shifts;

public class OneTimeBillingTests
{
    private readonly Guid _shiftId = Guid.NewGuid();
    private readonly Mock<IShiftRepository> _shifts = new();
    private readonly Mock<IShiftBillingRepository> _billing = new();
    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<ITenantProfileRepository> _profiles = new();
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IShiftWorkReservation> _reservation = new();

    public OneTimeBillingTests()
    {
        _invoices.Setup(i => i.ListAsync(null, null, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<CleaningSuite.Domain.Invoicing.Invoice>());
        _billing.Setup(b => b.GetAsync(_shiftId, It.IsAny<CancellationToken>())).ReturnsAsync((ShiftBillingProfile?)null);
        _reservation.Setup(r => r.AcquireAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new Lease());
        _tenant.SetupGet(t => t.TenantId).Returns("readysetsiivous");
        _profiles.Setup(p => p.GetAsync("readysetsiivous", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantProfile { TimeZoneId = "Europe/Helsinki" });
    }

    private ShiftBillingSettingsService Service(ShiftSchedule schedule)
    {
        _shifts.Setup(s => s.GetAsync(_shiftId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Shift { Id = _shiftId, Name = "Visit", IsActive = true, Schedule = schedule });
        return new ShiftBillingSettingsService(_shifts.Object, _billing.Object, _invoices.Object,
            _profiles.Object, _tenant.Object, _reservation.Object);
    }

    private static ShiftSchedule OneTime() => new()
    {
        Type = ShiftScheduleType.OneTime,
        OneTimeDate = new DateTime(2026, 10, 1),
        OneTimeStart = new TimeSpan(9, 0, 0),
        OneTimeEnd = new TimeSpan(11, 30, 0),
    };

    private static ShiftSchedule Weekly() => new()
    {
        Type = ShiftScheduleType.Weekly,
        WeeklyDay = DayOfWeek.Friday,
        WeeklyStart = new TimeSpan(16, 0, 0),
        WeeklyEnd = new TimeSpan(18, 0, 0),
    };

    [Fact]
    public async Task PerCompletedVisit_pricing_saves_for_one_time_shift()
    {
        var service = Service(OneTime());

        await service.SaveAsync(_shiftId, new ShiftPricingPlan("PerOccurrence", 40m, 25.5m, new DateOnly(2026, 10, 1)), CancellationToken.None);

        _billing.Verify(b => b.SaveAsync(It.Is<ShiftBillingProfile>(p =>
            p.Plans.Single().Mode == "PerOccurrence" && p.Plans.Single().RateNet == 40m), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PerCompletedVisit_pricing_is_rejected_for_recurring_shift()
    {
        var service = Service(Weekly());

        var error = await Assert.ThrowsAsync<ValidationException>(() => service.SaveAsync(_shiftId,
            new ShiftPricingPlan("PerOccurrence", 40m, 25.5m, new DateOnly(2026, 10, 1)), CancellationToken.None));

        Assert.Contains("on-call and one-time", error.Errors.Single().ErrorMessage);
        _billing.Verify(b => b.SaveAsync(It.IsAny<ShiftBillingProfile>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Visit_price_override_is_rejected_for_recurring_shift_before_any_write()
    {
        var service = Service(Weekly());
        var price = new ShiftOccurrencePrice(new DateTime(2026, 10, 2, 13, 0, 0, DateTimeKind.Utc), 55m, "Agreed extra work");

        var error = await Assert.ThrowsAsync<ValidationException>(() => service.SaveOccurrenceAsync(_shiftId, price, CancellationToken.None));

        Assert.Contains("on-call and one-time", error.Errors.Single().ErrorMessage);
        _billing.Verify(b => b.SaveAsync(It.IsAny<ShiftBillingProfile>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class Lease : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
