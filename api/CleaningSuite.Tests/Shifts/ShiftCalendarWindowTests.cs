using CleaningSuite.Application.Shifts;
using FluentValidation;

namespace CleaningSuite.Tests.Shifts;

public class ShiftCalendarWindowTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.Utc;
    private static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Past_filter_covers_both_dates_inclusive()
    {
        var window = ShiftCalendarWindow.Create("past", null, Now, Zone, "2026-10-01", "2026-10-03");
        Assert.Equal("2026-10-01", window.FromLocalDate);
        Assert.Equal("2026-10-03", window.ToLocalDate);
        Assert.Equal(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), window.FromUtc);
        Assert.Equal(new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc), window.ToUtc);
    }

    [Fact]
    public void Missing_start_defaults_to_31_days_before_the_end_date()
    {
        var window = ShiftCalendarWindow.Create("past", null, Now, Zone, null, "2026-11-01");
        Assert.Equal("2026-10-01", window.FromLocalDate);
        Assert.Equal("2026-11-01", window.ToLocalDate);
    }

    [Fact]
    public void Inverted_range_is_rejected()
    {
        Assert.Throws<ValidationException>(() => ShiftCalendarWindow.Create("past", null, Now, Zone, "2026-10-05", "2026-10-01"));
    }

    [Fact]
    public void End_date_after_today_is_rejected_for_past_work()
    {
        Assert.Throws<ValidationException>(() => ShiftCalendarWindow.Create("past", null, Now, Zone, "2026-10-01", "2026-11-11"));
    }

    [Fact]
    public void Range_longer_than_a_year_is_rejected()
    {
        Assert.Throws<ValidationException>(() => ShiftCalendarWindow.Create("past", null, Now, Zone, "2025-01-01", "2026-11-01"));
    }

    [Fact]
    public void Malformed_date_is_rejected()
    {
        Assert.Throws<ValidationException>(() => ShiftCalendarWindow.Create("past", null, Now, Zone, "01.10.2026", null));
    }
}
