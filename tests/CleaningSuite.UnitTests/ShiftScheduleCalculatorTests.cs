using System;
using System.Collections.Generic;
using CleaningSuite.Domain.Shifts;
using Xunit;

namespace CleaningSuite.UnitTests;

public class ShiftScheduleCalculatorTests
{
    [Fact]
    public void DailySameTime_generates_occurrence_for_each_day()
    {
        var shift = new Shift
        {
            IsActive = true,
            Schedule = new ShiftSchedule
            {
                Type = ShiftScheduleType.DailySameTime,
                DailyStart = new TimeSpan(8, 0, 0),
                DailyEnd = new TimeSpan(16, 0, 0)
            }
        };

        var occurrences = ShiftScheduleCalculator.GenerateOccurrences(
            shift,
            new DateTime(2026, 3, 2, 0, 0, 0),
            new DateTime(2026, 3, 4, 0, 0, 0));

        Assert.Equal(3, occurrences.Count);
        Assert.Equal(new DateTime(2026, 3, 2, 8, 0, 0), occurrences[0].StartUtc);
        Assert.Equal(new DateTime(2026, 3, 4, 16, 0, 0), occurrences[^1].EndUtc);
    }

    [Fact]
    public void Weekly_generates_occurrence_only_on_selected_day()
    {
        var shift = new Shift
        {
            IsActive = true,
            Schedule = new ShiftSchedule
            {
                Type = ShiftScheduleType.Weekly,
                WeeklyDay = DayOfWeek.Monday,
                WeeklyStart = new TimeSpan(9, 0, 0),
                WeeklyEnd = new TimeSpan(17, 0, 0)
            }
        };

        var occurrences = ShiftScheduleCalculator.GenerateOccurrences(
            shift,
            new DateTime(2026, 3, 2, 0, 0, 0), // Monday
            new DateTime(2026, 3, 8, 0, 0, 0)); // Sunday

        Assert.Single(occurrences);
        Assert.Equal(DayOfWeek.Monday, occurrences[0].StartUtc.DayOfWeek);
    }

    [Fact]
    public void Overlap_detects_intersection()
    {
        var a = new ShiftOccurrence { StartUtc = new DateTime(2026, 3, 2, 8, 0, 0), EndUtc = new DateTime(2026, 3, 2, 10, 0, 0) };
        var b = new ShiftOccurrence { StartUtc = new DateTime(2026, 3, 2, 9, 0, 0), EndUtc = new DateTime(2026, 3, 2, 11, 0, 0) };

        Assert.True(ShiftScheduleCalculator.Overlaps(a, b));
    }
}
