using System;
using System.Linq;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Domain.Shifts;
using TimeZoneConverter;
using Xunit;

namespace CleaningSuite.Tests.Shifts;

public class OneTimeVisibilityTests
{
    private static TimeZoneInfo Helsinki => TZConvert.GetTimeZoneInfo("Europe/Helsinki");

    // Entered on 2026-11-03 for a visit on 2026-10-01 (in the past).
    private static Shift Entered(ShiftSchedule schedule)
    {
        var shift = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Visit", schedule, null, null, null);
        shift.CreatedUtc = new DateTime(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc);
        return shift;
    }

    private static ShiftSchedule OneTime(DateTime date) => new()
    {
        Type = ShiftScheduleType.OneTime,
        OneTimeDate = date,
        OneTimeStart = new TimeSpan(9, 0, 0),
        OneTimeEnd = new TimeSpan(11, 30, 0),
    };

    private static readonly DateTime WindowFrom = new(2026, 9, 30, 21, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime WindowTo = new(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Past_one_time_visit_entered_later_is_still_visible_to_assignment_and_work()
    {
        var shift = Entered(OneTime(new DateTime(2026, 10, 1)));

        var visits = QualityCycleOccurrences.InWindow(shift, WindowFrom, WindowTo, Helsinki, includeWithoutChecklist: true);

        var visit = Assert.Single(visits);
        Assert.Equal(new DateTime(2026, 10, 1, 6, 0, 0, DateTimeKind.Utc), visit.Occurrence.StartUtc);
        Assert.Equal(new DateTime(2026, 10, 1, 8, 30, 0, DateTimeKind.Utc), visit.Occurrence.EndUtc);
    }

    [Fact]
    public void Recurring_past_occurrence_entered_later_is_not_invented()
    {
        // Historical protection: recurring shifts never gain visits before their creation.
        var shift = Entered(new ShiftSchedule
        {
            Type = ShiftScheduleType.DailySameTime,
            DailyStart = new TimeSpan(8, 0, 0),
            DailyEnd = new TimeSpan(16, 0, 0),
        });

        var visits = QualityCycleOccurrences.InWindow(shift, WindowFrom, WindowTo, Helsinki, includeWithoutChecklist: true);

        Assert.Empty(visits);
    }

    [Fact]
    public void One_time_visit_outside_the_queried_window_is_not_returned()
    {
        var shift = Entered(OneTime(new DateTime(2026, 10, 15)));

        var visits = QualityCycleOccurrences.InWindow(shift, WindowFrom, WindowTo, Helsinki, includeWithoutChecklist: true);

        Assert.Empty(visits);
    }

    [Fact]
    public void Inactive_one_time_shift_is_not_visible()
    {
        var shift = Entered(OneTime(new DateTime(2026, 10, 1)));
        shift.Deactivate();

        var visits = QualityCycleOccurrences.InWindow(shift, WindowFrom, WindowTo, Helsinki, includeWithoutChecklist: true);

        Assert.Empty(visits);
    }

    [Fact]
    public void Past_one_time_visit_stays_visible_when_its_first_plan_is_recorded_at_entry()
    {
        // Entering a template records plan history at creation time; that first plan must still show the visit once.
        var shift = Entered(OneTime(new DateTime(2026, 10, 1)));
        shift.RecordQualityCyclePlan(new DateTime(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc));

        var visits = QualityCycleOccurrences.InWindow(shift, WindowFrom, WindowTo, Helsinki, includeWithoutChecklist: true);

        Assert.Single(visits);
    }
}
