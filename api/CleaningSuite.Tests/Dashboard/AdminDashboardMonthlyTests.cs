using CleaningSuite.Application.Dashboard;

namespace CleaningSuite.Tests.Dashboard;

public class AdminDashboardMonthlyTests
{
    private static readonly TimeZoneInfo Helsinki = TimeZoneInfo.FindSystemTimeZoneById("Europe/Helsinki");
    private static readonly Guid Alpha = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Beta = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly IReadOnlyDictionary<Guid, string> Names = new Dictionary<Guid, string> { [Alpha] = "Alpha Oy", [Beta] = "Beta Ltd" };

    [Fact]
    public void Visits_are_counted_in_the_tenant_month_and_customers_are_sorted_by_name()
    {
        // 31 Oct 23:30 UTC is already 1 Nov in Helsinki, so it belongs to November.
        var visits = new[] {
            new DashboardVisitInput(Beta, new DateTime(2026, 10, 31, 23, 30, 0, DateTimeKind.Utc), new DateTime(2026, 11, 1, 1, 30, 0, DateTimeKind.Utc)),
            new DashboardVisitInput(Alpha, new DateTime(2026, 10, 10, 6, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 10, 8, 0, 0, DateTimeKind.Utc)),
            new DashboardVisitInput(Beta, new DateTime(2026, 10, 12, 6, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 12, 7, 0, 0, DateTimeKind.Utc)) };

        var months = DashboardMonthlyRules.Build(visits, [], Helsinki, Names);

        Assert.Equal(["2026-10", "2026-11"], months.Select(m => m.Month).ToArray());
        Assert.Equal(["Alpha Oy", "Beta Ltd"], months[0].Customers.Select(c => c.CustomerName).ToArray());
        Assert.Equal(2m, months[0].Customers.Single(c => c.CompanyId == Alpha).ScheduledHours);
        Assert.Equal(1, months[1].Customers.Single().ScheduledVisits);
    }

    [Fact]
    public void Actual_hours_come_only_from_submitted_records_with_a_valid_time_range()
    {
        var visit = new DateTime(2026, 10, 10, 6, 0, 0, DateTimeKind.Utc);
        var work = new[] {
            new DashboardWorkInput(Alpha, visit, new DateTime(2026, 10, 10, 6, 5, 0, DateTimeKind.Utc), new DateTime(2026, 10, 10, 8, 35, 0, DateTimeKind.Utc), true),
            new DashboardWorkInput(Alpha, visit.AddDays(1), new DateTime(2026, 10, 11, 9, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 11, 8, 0, 0, DateTimeKind.Utc), true),
            new DashboardWorkInput(Alpha, visit.AddDays(2), new DateTime(2026, 10, 12, 6, 0, 0, DateTimeKind.Utc), null, false) };

        var alpha = Assert.Single(DashboardMonthlyRules.Build([], work, Helsinki, Names)).Customers.Single();

        // Both submitted records count as completed work; only the reversed time contributes no hours.
        Assert.Equal(2, alpha.CompletedVisits);
        Assert.Equal(2.5m, alpha.ActualHours);
        Assert.Equal(0, alpha.ScheduledVisits);
    }
}
