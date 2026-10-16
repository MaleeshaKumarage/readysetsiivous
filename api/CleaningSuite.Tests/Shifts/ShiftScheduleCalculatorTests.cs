using System;
using System.Collections.Generic;
using CleaningSuite.Domain.Shifts;
using Xunit;

namespace CleaningSuite.Tests.Shifts;

public class ShiftScheduleCalculatorTests
{
    [Fact]
    public void Calculate_Throws_WhenScheduleIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => ShiftScheduleCalculator.Calculate(null!, DateTime.UtcNow, DateTime.UtcNow.AddDays(1)));
    }

    [Fact]
    public void GenerateOccurrences_Throws_WhenShiftIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => ShiftScheduleCalculator.GenerateOccurrences((Shift)null!, DateTime.UtcNow, DateTime.UtcNow.AddDays(1)));
    }

    [Fact]
    public void HasOverlap_Throws_WhenEitherCollectionIsNull()
    {
        var list = new List<ShiftOccurrence>();
        Assert.Throws<ArgumentNullException>(() => ShiftScheduleCalculator.HasOverlap(null!, list));
        Assert.Throws<ArgumentNullException>(() => ShiftScheduleCalculator.HasOverlap(list, null!));
    }

    [Fact]
    public void DailySameTime_Generates_Occurrence_For_Each_Day()
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
        Assert.Equal(new DateTime(2026, 3, 2, 8, 0, 0, DateTimeKind.Utc), occurrences[0].StartUtc);
        Assert.Equal(new DateTime(2026, 3, 4, 16, 0, 0, DateTimeKind.Utc), occurrences[^1].EndUtc);
    }

    [Fact]
    public void Occurrences_Convert_Tenant_WallClock_Times_To_Utc()
    {
        var schedule = new ShiftSchedule { Type = ShiftScheduleType.DailySameTime,
            DailyStart = TimeSpan.FromHours(8), DailyEnd = TimeSpan.FromHours(10) };
        var zone = TimeZoneInfo.CreateCustomTimeZone("tenant-plus-three", TimeSpan.FromHours(3), "Tenant +03", "Tenant +03");
        var occurrence = Assert.Single(ShiftScheduleCalculator.Calculate(schedule,
            new DateTime(2026, 1, 15), new DateTime(2026, 1, 15), zone));
        Assert.Equal(new DateTime(2026, 1, 15, 5, 0, 0, DateTimeKind.Utc), occurrence.StartUtc);
        Assert.Equal(new DateTime(2026, 1, 15, 7, 0, 0, DateTimeKind.Utc), occurrence.EndUtc);
    }

    [Fact]
    public void Occurrence_On_SpringForward_Gap_Is_Omitted()
    {
        var schedule = new ShiftSchedule { Type = ShiftScheduleType.DailySameTime,
            DailyStart = TimeSpan.FromHours(2.5), DailyEnd = TimeSpan.FromHours(4) };
        var start = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 29);
        var end = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 25);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31),
            TimeSpan.FromHours(1), start, end);
        var zone = TimeZoneInfo.CreateCustomTimeZone("test-dst", TimeSpan.FromHours(2), "Test DST", "Test Standard", "Test Daylight", new[] { rule });
        Assert.Empty(ShiftScheduleCalculator.Calculate(schedule, new DateTime(2026, 3, 29), new DateTime(2026, 3, 29), zone));
    }

    [Fact]
    public void Ambiguous_FallBack_Time_Uses_First_Clock_Occurrence()
    {
        var schedule = new ShiftSchedule { Type = ShiftScheduleType.DailySameTime,
            DailyStart = new TimeSpan(1, 30, 0), DailyEnd = new TimeSpan(2, 30, 0) };
        var start = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 29);
        var end = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0), 10, 25);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31),
            TimeSpan.FromHours(1), start, end);
        var zone = TimeZoneInfo.CreateCustomTimeZone("test-dst", TimeSpan.Zero, "Test DST", "Test Standard", "Test Daylight", new[] { rule });
        var occurrence = Assert.Single(ShiftScheduleCalculator.Calculate(schedule,
            new DateTime(2026, 10, 25), new DateTime(2026, 10, 25), zone));
        Assert.Equal(new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc), occurrence.StartUtc);
    }

    [Fact]
    public void DailySameTime_ReturnsEmpty_WhenStartOrEndIsNull()
    {
        var schedule = new ShiftSchedule
        {
            Type = ShiftScheduleType.DailySameTime,
            DailyStart = null,
            DailyEnd = new TimeSpan(16, 0, 0)
        };

        var occurrences = ShiftScheduleCalculator.Calculate(schedule, new DateTime(2026, 3, 2), new DateTime(2026, 3, 4));
        Assert.Empty(occurrences);
    }

    [Fact]
    public void DailyDifferentTime_Generates_Occurrences_For_Configured_Days()
    {
        var schedule = new ShiftSchedule
        {
            Type = ShiftScheduleType.DailyDifferentTime,
            DailyTimes = new Dictionary<DayOfWeek, TimeRange>
            {
                [DayOfWeek.Monday] = new TimeRange { Start = new TimeSpan(8, 0, 0), End = new TimeSpan(12, 0, 0) },
                [DayOfWeek.Wednesday] = new TimeRange { Start = new TimeSpan(13, 0, 0), End = new TimeSpan(17, 0, 0) }
            }
        };

        var occurrences = ShiftScheduleCalculator.Calculate(
            schedule,
            new DateTime(2026, 3, 2), // Monday
            new DateTime(2026, 3, 4)); // Wednesday

        Assert.Equal(2, occurrences.Count);
        Assert.Equal(DayOfWeek.Monday, occurrences[0].StartUtc.DayOfWeek);
        Assert.Equal(DayOfWeek.Wednesday, occurrences[1].StartUtc.DayOfWeek);
    }

    [Fact]
    public void Weekly_Generates_Occurrence_Only_On_Selected_Day()
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
    public void Weekly_MultipleDays_Generates_Occurrences_For_All_Selected_Days()
    {
        var schedule = new ShiftSchedule
        {
            Type = ShiftScheduleType.Weekly,
            WeeklyDays = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Friday },
            WeeklyStart = new TimeSpan(9, 0, 0),
            WeeklyEnd = new TimeSpan(17, 0, 0)
        };

        var occurrences = ShiftScheduleCalculator.Calculate(
            schedule,
            new DateTime(2026, 3, 2), // Monday
            new DateTime(2026, 3, 8)); // Sunday

        Assert.Equal(2, occurrences.Count);
        Assert.Equal(DayOfWeek.Monday, occurrences[0].StartUtc.DayOfWeek);
        Assert.Equal(DayOfWeek.Friday, occurrences[1].StartUtc.DayOfWeek);
    }

    [Fact]
    public void Weekly_ReturnsEmpty_WhenStartOrEndIsNull()
    {
        var schedule = new ShiftSchedule
        {
            Type = ShiftScheduleType.Weekly,
            WeeklyDay = DayOfWeek.Monday,
            WeeklyStart = null,
            WeeklyEnd = new TimeSpan(17, 0, 0)
        };

        var occurrences = ShiftScheduleCalculator.Calculate(schedule, new DateTime(2026, 3, 2), new DateTime(2026, 3, 8));
        Assert.Empty(occurrences);
    }

    [Fact]
    public void BiWeekly_Parity_One_Includes_Anchor_Week()
    {
        var shift = BiWeeklyShift(parity: 1);

        var occurrences = ShiftScheduleCalculator.GenerateOccurrences(
            shift,
            new DateTime(2024, 1, 1, 0, 0, 0), // anchor week (2024-01-01 is a Monday)
            new DateTime(2024, 1, 8, 0, 0, 0));

        Assert.Single(occurrences);
        Assert.Equal(new DateTime(2024, 1, 1, 9, 0, 0, DateTimeKind.Utc), occurrences[0].StartUtc);
    }

    [Fact]
    public void BiWeekly_Parity_Two_Skips_Anchor_Week()
    {
        var shift = BiWeeklyShift(parity: 2);

        var occurrences = ShiftScheduleCalculator.GenerateOccurrences(
            shift,
            new DateTime(2024, 1, 1, 0, 0, 0), // anchor week
            new DateTime(2024, 1, 7, 0, 0, 0));

        Assert.Empty(occurrences);
    }

    [Fact]
    public void BiWeekly_Parity_Two_Includes_Week_Before_Anchor()
    {
        var shift = BiWeeklyShift(parity: 2);

        var occurrences = ShiftScheduleCalculator.GenerateOccurrences(
            shift,
            new DateTime(2023, 12, 25, 0, 0, 0), // one week before the anchor
            new DateTime(2024, 1, 1, 0, 0, 0));

        Assert.Single(occurrences);
        Assert.Equal(new DateTime(2023, 12, 25, 9, 0, 0, DateTimeKind.Utc), occurrences[0].StartUtc);
    }

    [Fact]
    public void BiWeekly_ReturnsEmpty_WhenStartOrEndIsNull()
    {
        var schedule = new ShiftSchedule
        {
            Type = ShiftScheduleType.BiWeekly,
            BiWeeklyWeekParity = 1,
            BiWeeklyDay = DayOfWeek.Monday,
            BiWeeklyStart = null,
            BiWeeklyEnd = new TimeSpan(17, 0, 0)
        };

        var occurrences = ShiftScheduleCalculator.Calculate(schedule, new DateTime(2024, 1, 1), new DateTime(2024, 1, 8));
        Assert.Empty(occurrences);
    }

    [Fact]
    public void Monthly_Generates_Occurrences_On_Specified_Day_Of_Month()
    {
        var schedule = new ShiftSchedule
        {
            Type = ShiftScheduleType.Monthly,
            MonthlyDay = 15,
            MonthlyStart = new TimeSpan(8, 0, 0),
            MonthlyEnd = new TimeSpan(16, 0, 0)
        };

        var occurrences = ShiftScheduleCalculator.Calculate(
            schedule,
            new DateTime(2026, 1, 1),
            new DateTime(2026, 3, 31));

        Assert.Equal(3, occurrences.Count);
        Assert.Equal(new DateTime(2026, 1, 15, 8, 0, 0, DateTimeKind.Utc), occurrences[0].StartUtc);
        Assert.Equal(new DateTime(2026, 2, 15, 8, 0, 0, DateTimeKind.Utc), occurrences[1].StartUtc);
        Assert.Equal(new DateTime(2026, 3, 15, 8, 0, 0, DateTimeKind.Utc), occurrences[2].StartUtc);
    }

    [Fact]
    public void Monthly_ReturnsEmpty_WhenStartOrEndIsNull()
    {
        var schedule = new ShiftSchedule
        {
            Type = ShiftScheduleType.Monthly,
            MonthlyDay = 15,
            MonthlyStart = null,
            MonthlyEnd = new TimeSpan(16, 0, 0)
        };

        var occurrences = ShiftScheduleCalculator.Calculate(schedule, new DateTime(2026, 1, 1), new DateTime(2026, 3, 31));
        Assert.Empty(occurrences);
    }

    [Fact]
    public void OnCallFlexible_Generates_No_Occurrences()
    {
        var schedule = new ShiftSchedule
        {
            Type = ShiftScheduleType.OnCallFlexible
        };

        var occurrences = ShiftScheduleCalculator.Calculate(schedule, new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        Assert.Empty(occurrences);
    }

    [Fact]
    public void Overlap_Detects_Intersection()
    {
        var a = new ShiftOccurrence { StartUtc = new DateTime(2026, 3, 2, 8, 0, 0), EndUtc = new DateTime(2026, 3, 2, 10, 0, 0) };
        var b = new ShiftOccurrence { StartUtc = new DateTime(2026, 3, 2, 9, 0, 0), EndUtc = new DateTime(2026, 3, 2, 11, 0, 0) };

        Assert.True(ShiftScheduleCalculator.HasOverlap(new[] { a }, new[] { b }));
    }

    [Fact]
    public void Non_Overlapping_Occurrences_Do_Not_Overlap()
    {
        var a = new ShiftOccurrence { StartUtc = new DateTime(2026, 3, 2, 8, 0, 0), EndUtc = new DateTime(2026, 3, 2, 10, 0, 0) };
        var b = new ShiftOccurrence { StartUtc = new DateTime(2026, 3, 2, 11, 0, 0), EndUtc = new DateTime(2026, 3, 2, 13, 0, 0) };

        Assert.False(ShiftScheduleCalculator.HasOverlap(new[] { a }, new[] { b }));
    }

    [Fact]
    public void HasOverlap_ReturnsFalse_WhenEitherListIsEmpty()
    {
        var occurrence = new ShiftOccurrence { StartUtc = new DateTime(2026, 3, 2, 8, 0, 0), EndUtc = new DateTime(2026, 3, 2, 10, 0, 0) };
        Assert.False(ShiftScheduleCalculator.HasOverlap(Array.Empty<ShiftOccurrence>(), new[] { occurrence }));
        Assert.False(ShiftScheduleCalculator.HasOverlap(new[] { occurrence }, Array.Empty<ShiftOccurrence>()));
    }

    [Fact]
    public void HasOverlap_TwoPointer_Handles_Large_Sorted_NonOverlapping_Lists()
    {
        var list1 = new List<ShiftOccurrence>();
        var list2 = new List<ShiftOccurrence>();
        var baseDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        for (int i = 0; i < 180; i++)
        {
            list1.Add(new ShiftOccurrence { StartUtc = baseDate.AddDays(i).AddHours(8), EndUtc = baseDate.AddDays(i).AddHours(12) });
            list2.Add(new ShiftOccurrence { StartUtc = baseDate.AddDays(i).AddHours(13), EndUtc = baseDate.AddDays(i).AddHours(17) });
        }

        Assert.False(ShiftScheduleCalculator.HasOverlap(list1, list2));
    }

    [Fact]
    public void HasOverlap_TwoPointer_Detects_Overlap_In_Large_Sorted_Lists()
    {
        var list1 = new List<ShiftOccurrence>();
        var list2 = new List<ShiftOccurrence>();
        var baseDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        for (int i = 0; i < 180; i++)
        {
            list1.Add(new ShiftOccurrence { StartUtc = baseDate.AddDays(i).AddHours(8), EndUtc = baseDate.AddDays(i).AddHours(12) });
            // Day 100 has an overlap
            var endHour = i == 100 ? 10 : 17;
            var startHour = i == 100 ? 9 : 13;
            list2.Add(new ShiftOccurrence { StartUtc = baseDate.AddDays(i).AddHours(startHour), EndUtc = baseDate.AddDays(i).AddHours(endHour) });
        }

        Assert.True(ShiftScheduleCalculator.HasOverlap(list1, list2));
    }

    [Fact]
    public void HasOverlap_Fallback_Handles_Unsorted_Lists()
    {
        var a1 = new ShiftOccurrence { StartUtc = new DateTime(2026, 3, 5, 8, 0, 0), EndUtc = new DateTime(2026, 3, 5, 10, 0, 0) };
        var a2 = new ShiftOccurrence { StartUtc = new DateTime(2026, 3, 2, 8, 0, 0), EndUtc = new DateTime(2026, 3, 2, 10, 0, 0) }; // unsorted

        var b1 = new ShiftOccurrence { StartUtc = new DateTime(2026, 3, 2, 9, 0, 0), EndUtc = new DateTime(2026, 3, 2, 11, 0, 0) }; // overlaps a2

        Assert.True(ShiftScheduleCalculator.HasOverlap(new[] { a1, a2 }, new[] { b1 }));
    }

    [Fact]
    public void Adjacent_Occurrences_Do_Not_Overlap()
    {
        var a = new ShiftOccurrence { StartUtc = new DateTime(2026, 3, 2, 8, 0, 0), EndUtc = new DateTime(2026, 3, 2, 10, 0, 0) };
        var b = new ShiftOccurrence { StartUtc = new DateTime(2026, 3, 2, 10, 0, 0), EndUtc = new DateTime(2026, 3, 2, 12, 0, 0) };

        Assert.False(ShiftScheduleCalculator.HasOverlap(new[] { a }, new[] { b }));
    }

    [Fact]
    public void HasOverlap_Preserves_Point_And_Reversed_Interval_Behavior()
    {
        var origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        ShiftOccurrence Interval(int start, int end) => new() { StartUtc = origin.AddHours(start), EndUtc = origin.AddHours(end) };
        var point = new[] { Interval(10, 10) };
        var containing = new[] { Interval(5, 10), Interval(6, 20) };
        Assert.True(ShiftScheduleCalculator.HasOverlap(point, containing));
        Assert.True(ShiftScheduleCalculator.HasOverlap(containing, point));
        Assert.True(ShiftScheduleCalculator.HasOverlap(new[] { Interval(11, 10) }, containing));
    }

    [Fact]
    public void HasOverlap_Matches_Pairwise_Oracle_For_Nested_Unsorted_And_Degenerate_Intervals()
    {
        var random = new Random(6241);
        var origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        ShiftOccurrence[] Generate(bool positive) => Enumerable.Range(0, random.Next(0, 20))
            .Select(_ => { var start = random.Next(0, 60); return new ShiftOccurrence {
                StartUtc = origin.AddMinutes(start), EndUtc = origin.AddMinutes(start + random.Next(positive ? 1 : -5, 20))
            }; }).ToArray();
        for (var iteration = 0; iteration < 1000; iteration++)
        {
            var first = Generate(iteration % 2 == 0);
            var second = Generate(iteration % 2 == 0);
            if (iteration % 3 != 0) { first = first.OrderBy(x => x.StartUtc).ToArray(); second = second.OrderBy(x => x.StartUtc).ToArray(); }
            var expected = first.Any(a => second.Any(b => a.StartUtc < b.EndUtc && b.StartUtc < a.EndUtc));
            Assert.Equal(expected, ShiftScheduleCalculator.HasOverlap(first, second));
            Assert.Equal(expected, ShiftScheduleCalculator.HasOverlap(second, first));
        }
    }

    private static Shift BiWeeklyShift(int parity) => new Shift
    {
        IsActive = true,
        Schedule = new ShiftSchedule
        {
            Type = ShiftScheduleType.BiWeekly,
            BiWeeklyWeekParity = parity,
            BiWeeklyDay = DayOfWeek.Monday,
            BiWeeklyStart = new TimeSpan(9, 0, 0),
            BiWeeklyEnd = new TimeSpan(17, 0, 0)
        }
    };
}
