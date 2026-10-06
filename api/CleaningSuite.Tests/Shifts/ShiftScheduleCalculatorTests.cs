using System;
using System.Collections.Generic;
using CleaningSuite.Domain.Shifts;
using Xunit;

namespace CleaningSuite.Tests.Shifts;

public class ShiftScheduleCalculatorTests
{
    [Fact]
    public void HasOverlap_NullOrEmptyLists_ReturnsFalse()
    {
        var baseDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var list = new List<ShiftOccurrence>
        {
            new ShiftOccurrence { StartUtc = baseDate.AddHours(9), EndUtc = baseDate.AddHours(12) }
        };

        Assert.False(ShiftScheduleCalculator.HasOverlap(new List<ShiftOccurrence>(), list));
        Assert.False(ShiftScheduleCalculator.HasOverlap(list, new List<ShiftOccurrence>()));
    }

    [Fact]
    public void HasOverlap_DetectsOverlappingOccurrences()
    {
        var baseDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var listA = new List<ShiftOccurrence>
        {
            new ShiftOccurrence { StartUtc = baseDate.AddHours(9), EndUtc = baseDate.AddHours(12) }
        };

        var listB = new List<ShiftOccurrence>
        {
            new ShiftOccurrence { StartUtc = baseDate.AddHours(11), EndUtc = baseDate.AddHours(15) }
        };

        Assert.True(ShiftScheduleCalculator.HasOverlap(listA, listB));
        Assert.True(ShiftScheduleCalculator.HasOverlap(listB, listA));
    }

    [Fact]
    public void HasOverlap_ReturnsFalseWhenTouchingOrDisjoint()
    {
        var baseDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var listA = new List<ShiftOccurrence>
        {
            new ShiftOccurrence { StartUtc = baseDate.AddHours(9), EndUtc = baseDate.AddHours(12) }
        };

        var listB = new List<ShiftOccurrence>
        {
            new ShiftOccurrence { StartUtc = baseDate.AddHours(12), EndUtc = baseDate.AddHours(15) },
            new ShiftOccurrence { StartUtc = baseDate.AddHours(16), EndUtc = baseDate.AddHours(18) }
        };

        Assert.False(ShiftScheduleCalculator.HasOverlap(listA, listB));
        Assert.False(ShiftScheduleCalculator.HasOverlap(listB, listA));
    }

    [Fact]
    public void HasOverlap_UnsortedLists_DetectsOverlapCorrectly()
    {
        var baseDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var listA = new List<ShiftOccurrence>
        {
            new ShiftOccurrence { StartUtc = baseDate.AddDays(5), EndUtc = baseDate.AddDays(5).AddHours(4) },
            new ShiftOccurrence { StartUtc = baseDate.AddDays(1), EndUtc = baseDate.AddDays(1).AddHours(4) }
        };

        var listB = new List<ShiftOccurrence>
        {
            new ShiftOccurrence { StartUtc = baseDate.AddDays(1).AddHours(2), EndUtc = baseDate.AddDays(1).AddHours(6) }
        };

        Assert.True(ShiftScheduleCalculator.HasOverlap(listA, listB));
    }
}
