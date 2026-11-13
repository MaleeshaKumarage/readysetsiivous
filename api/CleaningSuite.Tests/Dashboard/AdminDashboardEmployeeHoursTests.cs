using CleaningSuite.Application.Dashboard;

namespace CleaningSuite.Tests.Dashboard;

public class AdminDashboardEmployeeHoursTests
{
    private static readonly Guid Anna = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Ben = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid Cia = Guid.Parse("00000000-0000-0000-0000-00000000000c");
    private static readonly Guid Alpha = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Beta = Guid.Parse("00000000-0000-0000-0000-0000000000b1");

    [Fact]
    public void Scheduled_hours_are_split_by_customer_and_a_shared_visit_counts_for_each_person()
    {
        var start = new DateTime(2026, 11, 9, 8, 0, 0, DateTimeKind.Utc);
        (Guid, IReadOnlyList<Guid>, DateTime, DateTime, bool)[] visits =
        [
            (Alpha, [Anna], start, start.AddHours(3), false),
            (Beta, [Anna, Ben], start.AddDays(1), start.AddDays(1).AddHours(2), false),
            (Beta, [Ben], start.AddDays(2), start.AddDays(2).AddHours(4), true),
        ];
        var names = new Dictionary<Guid, string> { [Alpha] = "Alpha Oy", [Beta] = "Beta Ltd" };
        var rows = DashboardEmployeeHoursRules.Build(visits, [(Anna, "Anna Virta"), (Ben, "Ben Koski"), (Cia, "Cia Lund")], names);

        var anna = rows.Single(r => r.EmployeeId == Anna);
        Assert.Equal(5m, anna.ScheduledHours);
        Assert.Equal(["Alpha Oy", "Beta Ltd"], anna.Companies.Select(c => c.CustomerName));
        Assert.Equal([3m, 2m], anna.Companies.Select(c => c.ScheduledHours));
        Assert.Equal(2m, rows.Single(r => r.EmployeeId == Ben).ScheduledHours);
        Assert.Empty(rows.Single(r => r.EmployeeId == Cia).Companies);
        Assert.Equal(37.5m, DashboardEmployeeHoursRules.FullTimeWeeklyHours);
    }

    [Fact]
    public void Each_employee_is_listed_once_by_name_even_with_no_visits()
    {
        (Guid, IReadOnlyList<Guid>, DateTime, DateTime, bool)[] none = [];
        var rows = DashboardEmployeeHoursRules.Build(none, [(Ben, "Ben Koski"), (Anna, "Anna Virta")], new Dictionary<Guid, string>());
        Assert.Equal(["Anna Virta", "Ben Koski"], rows.Select(r => r.Name));
        Assert.All(rows, r => Assert.Equal(0m, r.ScheduledHours));
    }
}
