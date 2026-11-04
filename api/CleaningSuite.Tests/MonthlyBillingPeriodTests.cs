using CleaningSuite.Application.Invoicing;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Domain.Invoicing;
using CleaningSuite.Domain.Shifts;

namespace CleaningSuite.Tests.Invoicing;

public class MonthlyBillingPeriodTests
{
    [Fact]
    public void A_monthly_price_for_a_full_month_is_not_prorated_by_the_span_of_completed_visits()
    {
        var branch = Guid.NewGuid();
        var shift = Shift.Create(Guid.NewGuid(), branch, "Monthly clean",
            new ShiftSchedule { Type = ShiftScheduleType.Weekly, WeeklyDays = [DayOfWeek.Wednesday], WeeklyStart = new(9, 0, 0), WeeklyEnd = new(10, 0, 0) },
            null, new DateTime(2026, 9, 1), new DateTime(2026, 9, 30));
        var profile = new ShiftBillingProfile { Plans = [new("Monthly", 100, 25.5m, new(2000, 1, 1))] };
        // September 2026 has 30 days; the first and last completed visits fall on the 2nd and the 29th.
        var visits = new[]
        {
            new BillableShiftVisit(new DateTime(2026, 9, 2, 6, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 2, 7, 0, 0, DateTimeKind.Utc), branch, "Monthly clean", true),
            new BillableShiftVisit(new DateTime(2026, 9, 29, 6, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 29, 7, 0, 0, DateTimeKind.Utc), branch, "Monthly clean", true),
        };
        var names = new Dictionary<Guid, string> { [branch] = "Lake apartment" };
        var lines = ShiftInvoiceCalculator.Calculate(shift, profile, visits, new(2026, 9, 2), new(2026, 9, 29), TimeZoneInfo.Utc, names, [],
            null, (new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)));
        var monthly = Assert.Single(lines, x => x.PricingMode == "Monthly");
        Assert.Equal(100m, monthly.NetAmount);
        Assert.Equal(1m, monthly.Quantity);
    }
}
