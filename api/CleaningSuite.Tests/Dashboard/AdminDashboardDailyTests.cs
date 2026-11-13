using CleaningSuite.Application.Dashboard;

namespace CleaningSuite.Tests.Dashboard;

public class AdminDashboardDailyTests
{
    private static readonly Guid Alpha = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Beta = Guid.Parse("00000000-0000-0000-0000-0000000000b1");

    [Fact]
    public void Each_day_of_the_month_is_listed_with_its_visits_hours_and_customers()
    {
        var zone = TimeZoneInfo.Utc;
        var firstDay = new DateOnly(2026, 11, 1);
        var visits = new[]
        {
            new DashboardVisitInput(Alpha, new DateTime(2026, 11, 3, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc)),
            new DashboardVisitInput(Alpha, new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 3, 13, 0, 0, DateTimeKind.Utc)),
            new DashboardVisitInput(Beta, new DateTime(2026, 11, 9, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc)),
        };
        var work = new[]
        {
            new DashboardWorkInput(Alpha, new DateTime(2026, 11, 3, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 3, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 3, 9, 30, 0, DateTimeKind.Utc), true),
            new DashboardWorkInput(Alpha, new DateTime(2026, 11, 4, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 4, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 4, 9, 0, 0, DateTimeKind.Utc), false),
        };
        var names = new Dictionary<Guid, string> { [Alpha] = "Alpha Oy", [Beta] = "Beta Ltd" };

        var days = DashboardDailyRules.Build(firstDay, 30, visits, work, zone, names);

        Assert.Equal(30, days.Count);
        Assert.Equal("2026-11-01", days[0].Date);
        var third = days[2];
        Assert.Equal(2, third.ScheduledVisits);
        Assert.Equal(3m, third.ScheduledHours);
        Assert.Equal(1.5m, third.ActualHours);
        Assert.Equal(["Alpha Oy"], third.Companies.Select(c => c.CustomerName));
        var ninth = days[8];
        Assert.Equal(1, ninth.ScheduledVisits);
        Assert.Equal(4m, ninth.ScheduledHours);
        Assert.Equal(0m, ninth.ActualHours);
        // A day with no visits or work is still listed, with zeros.
        Assert.Equal(0, days[15].ScheduledVisits);
        Assert.Empty(days[15].Companies);
    }
}
