using System;
using System.Linq;
using CleaningSuite.Domain.Shifts;
using TimeZoneConverter;
using Xunit;

namespace CleaningSuite.Tests.Shifts;

public class OneTimeShiftScheduleTests
{
    private static ShiftSchedule OneTime(DateTime date, TimeSpan start, TimeSpan end) => new()
    {
        Type = ShiftScheduleType.OneTime,
        OneTimeDate = date,
        OneTimeStart = start,
        OneTimeEnd = end,
    };

    private static TimeZoneInfo Helsinki => TZConvert.GetTimeZoneInfo("Europe/Helsinki");

    [Fact]
    public void OneTimeValue_IsAppendedWithoutRenumberingExistingTypes()
    {
        Assert.Equal(6, (int)ShiftScheduleType.OneTime);
        Assert.Equal(4, (int)ShiftScheduleType.OnCallFlexible);
        Assert.Equal(5, (int)ShiftScheduleType.Monthly);
    }

    [Fact]
    public void Validate_Accepts_CompleteOneTimeSchedule_OnPastDate()
    {
        var schedule = OneTime(new DateTime(2020, 1, 15), new TimeSpan(8, 0, 0), new TimeSpan(12, 0, 0));
        schedule.Validate();
    }

    [Fact]
    public void Validate_Rejects_MissingOneTimeValues()
    {
        Assert.Throws<ArgumentException>(() => new ShiftSchedule
        {
            Type = ShiftScheduleType.OneTime,
            OneTimeStart = new TimeSpan(8, 0, 0),
            OneTimeEnd = new TimeSpan(12, 0, 0),
        }.Validate());
        Assert.Throws<ArgumentException>(() => new ShiftSchedule
        {
            Type = ShiftScheduleType.OneTime,
            OneTimeDate = new DateTime(2026, 11, 3),
            OneTimeEnd = new TimeSpan(12, 0, 0),
        }.Validate());
        Assert.Throws<ArgumentException>(() => new ShiftSchedule
        {
            Type = ShiftScheduleType.OneTime,
            OneTimeDate = new DateTime(2026, 11, 3),
            OneTimeStart = new TimeSpan(8, 0, 0),
        }.Validate());
    }

    [Fact]
    public void Validate_Rejects_EndNotAfterStart()
    {
        var equal = OneTime(new DateTime(2026, 11, 3), new TimeSpan(8, 0, 0), new TimeSpan(8, 0, 0));
        Assert.Throws<ArgumentException>(() => equal.Validate());

        var reversed = OneTime(new DateTime(2026, 11, 3), new TimeSpan(12, 0, 0), new TimeSpan(8, 0, 0));
        Assert.Throws<ArgumentException>(() => reversed.Validate());
    }

    [Fact]
    public void Calculate_ReturnsExactlyOneOccurrence_WhenDateInsideInclusiveRange()
    {
        var date = new DateTime(2026, 11, 3);
        var schedule = OneTime(date, new TimeSpan(8, 0, 0), new TimeSpan(12, 0, 0));

        Assert.Single(ShiftScheduleCalculator.Calculate(schedule, date, date, Helsinki));
        Assert.Single(ShiftScheduleCalculator.Calculate(schedule, date.AddDays(-30), date.AddDays(30), Helsinki));
    }

    [Fact]
    public void Calculate_ReturnsNothing_WhenDateOutsideRange()
    {
        var date = new DateTime(2026, 11, 3);
        var schedule = OneTime(date, new TimeSpan(8, 0, 0), new TimeSpan(12, 0, 0));

        Assert.Empty(ShiftScheduleCalculator.Calculate(schedule, date.AddDays(1), date.AddDays(10), Helsinki));
        Assert.Empty(ShiftScheduleCalculator.Calculate(schedule, date.AddDays(-10), date.AddDays(-1), Helsinki));
    }

    [Fact]
    public void Calculate_ReturnsExactlyOneOccurrence_AcrossWideRange()
    {
        var date = new DateTime(2026, 11, 3);
        var schedule = OneTime(date, new TimeSpan(8, 0, 0), new TimeSpan(12, 0, 0));

        var occurrences = ShiftScheduleCalculator.Calculate(
            schedule, new DateTime(2000, 1, 1), new DateTime(2100, 12, 31), Helsinki);

        Assert.Single(occurrences);
    }

    [Fact]
    public void Calculate_ConvertsLocalTimeUsingTenantZone()
    {
        // 10:00 Helsinki summer time (UTC+3) is 07:00 UTC.
        var date = new DateTime(2026, 6, 1);
        var schedule = OneTime(date, new TimeSpan(10, 0, 0), new TimeSpan(12, 0, 0));

        var occurrence = Assert.Single(ShiftScheduleCalculator.Calculate(schedule, date, date, Helsinki));

        Assert.Equal(new DateTime(2026, 6, 1, 7, 0, 0, DateTimeKind.Utc), occurrence.StartUtc);
        Assert.Equal(new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc), occurrence.EndUtc);
    }

    [Fact]
    public void Calculate_IgnoresTimeComponentOfOneTimeDate()
    {
        var schedule = OneTime(new DateTime(2026, 11, 3, 23, 30, 0), new TimeSpan(8, 0, 0), new TimeSpan(12, 0, 0));
        var day = new DateTime(2026, 11, 3);

        Assert.Single(ShiftScheduleCalculator.Calculate(schedule, day, day, Helsinki));
    }

    [Fact]
    public void Shift_Create_NormalizesValidityToOneTimeDate_IgnoringStaleDates()
    {
        var date = new DateTime(2026, 11, 3);
        var schedule = OneTime(date, new TimeSpan(8, 0, 0), new TimeSpan(12, 0, 0));

        var shift = Shift.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Visit", schedule, null,
            validFrom: new DateTime(2026, 11, 1),
            validUntil: new DateTime(2026, 11, 2));

        Assert.Equal(date, shift.ValidFrom);
        Assert.Equal(date, shift.ValidUntil);
    }

    [Fact]
    public void Shift_Create_AllowsMissingValidity_ForOneTime()
    {
        var date = new DateTime(2020, 1, 15);
        var schedule = OneTime(date, new TimeSpan(8, 0, 0), new TimeSpan(12, 0, 0));

        var shift = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Visit", schedule, null, null, null);

        Assert.Equal(date, shift.ValidFrom);
        Assert.Equal(date, shift.ValidUntil);
    }

    [Fact]
    public void Shift_Update_NormalizesValidityToOneTimeDate()
    {
        var shift = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Recurring",
            new ShiftSchedule
            {
                Type = ShiftScheduleType.DailySameTime,
                DailyStart = new TimeSpan(8, 0, 0),
                DailyEnd = new TimeSpan(12, 0, 0),
            },
            null, new DateTime(2026, 1, 1), new DateTime(2026, 12, 31));

        var date = new DateTime(2026, 11, 3);
        shift.Update(shift.CompanyId, shift.BranchId, "Visit",
            OneTime(date, new TimeSpan(8, 0, 0), new TimeSpan(12, 0, 0)),
            null, true, new DateTime(2026, 1, 1), new DateTime(2026, 12, 31));

        Assert.Equal(date, shift.ValidFrom);
        Assert.Equal(date, shift.ValidUntil);
        Assert.Equal(1, ShiftScheduleCalculator.GenerateOccurrences(shift, new DateTime(2026, 1, 1), new DateTime(2026, 12, 31)).Count);
    }

    [Fact]
    public void Calculate_SpringForwardGap_ProducesNoOccurrence()
    {
        // 2026-03-29 02:00-03:00 local does not exist in Helsinki. The domain drops it silently;
        // the service layer must reject such a visit before saving (see the one-time service tests).
        var date = new DateTime(2026, 3, 29);
        var schedule = OneTime(date, new TimeSpan(3, 30, 0), new TimeSpan(5, 0, 0));

        Assert.Empty(ShiftScheduleCalculator.Calculate(schedule, date, date, Helsinki));
    }
}
