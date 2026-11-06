using System;
using CleaningSuite.Domain.Shifts;
using Xunit;

namespace CleaningSuite.Tests.Shifts;

public class ScheduleValidationMessageTests
{
    [Fact]
    public void Weekly_schedule_without_a_day_says_what_to_choose_and_never_names_an_internal_parameter()
    {
        var schedule = new ShiftSchedule { Type = ShiftScheduleType.Weekly, WeeklyStart = TimeSpan.FromHours(6), WeeklyEnd = TimeSpan.FromHours(8) };

        var error = Assert.Throws<ArgumentException>(() => schedule.Validate());

        Assert.Contains("Choose at least one weekday", error.Message);
        Assert.DoesNotContain("Parameter", error.Message);
    }

    [Fact]
    public void Weekly_schedule_ending_before_it_starts_tells_the_user_to_choose_a_later_end_time()
    {
        var schedule = new ShiftSchedule { Type = ShiftScheduleType.Weekly, WeeklyDays = [DayOfWeek.Monday], WeeklyStart = TimeSpan.FromHours(8), WeeklyEnd = TimeSpan.FromHours(6) };

        var error = Assert.Throws<ArgumentException>(() => schedule.Validate());

        Assert.Contains("Choose a later end time", error.Message);
        Assert.DoesNotContain("Parameter", error.Message);
    }
}
