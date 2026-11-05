using System;
using System.Linq;
using CleaningSuite.Application.Shifts;
using Xunit;

namespace CleaningSuite.Tests.Shifts;

public class EmployeeScheduleTests
{
    private static readonly TimeZoneInfo Helsinki = TimeZoneInfo.FindSystemTimeZoneById("Europe/Helsinki");
    private static ShiftOverlapVisit Visit(string name, DateTime startUtc) => new(Guid.NewGuid(), name, startUtc, startUtc.AddHours(2));

    [Fact]
    public void Visits_are_grouped_per_cleaner_and_local_day_in_visit_order()
    {
        var cleaner = Guid.NewGuid();
        var morning = Visit("Morning", new DateTime(2026, 11, 2, 6, 0, 0, DateTimeKind.Utc));
        var afternoon = Visit("Afternoon", new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc));
        var nextDay = Visit("Next day", new DateTime(2026, 11, 3, 6, 0, 0, DateTimeKind.Utc));
        var days = ScheduleVisits_Group([(cleaner, afternoon), (cleaner, nextDay), (cleaner, morning)]);
        Assert.Equal(2, days.Count);
        Assert.Equal(new DateTime(2026, 11, 2), days[0].LocalDay);
        Assert.Equal(new[] { morning, afternoon }, days[0].Visits);
        Assert.Equal(new[] { nextDay }, days[1].Visits);
    }

    [Fact]
    public void A_visit_late_in_the_UTC_day_belongs_to_the_next_local_day()
    {
        var cleaner = Guid.NewGuid();
        // 22:30 UTC on 2 Nov is 00:30 on 3 Nov in Helsinki (UTC+2 in November).
        var lateNight = Visit("Late", new DateTime(2026, 11, 2, 22, 30, 0, DateTimeKind.Utc));
        var days = ScheduleVisits_Group([(cleaner, lateNight)]);
        Assert.Equal(new DateTime(2026, 11, 3), Assert.Single(days).LocalDay);
    }

    [Fact]
    public void Cleaners_with_no_visits_do_not_appear()
    {
        Assert.Empty(ScheduleVisits_Group([]));
    }

    private static System.Collections.Generic.IReadOnlyList<EmployeeDaySchedule> ScheduleVisits_Group(
        System.Collections.Generic.IEnumerable<(Guid EmployeeId, ShiftOverlapVisit Visit)> visits) =>
        ScheduleVisits.GroupByDay(visits, Helsinki);
}
