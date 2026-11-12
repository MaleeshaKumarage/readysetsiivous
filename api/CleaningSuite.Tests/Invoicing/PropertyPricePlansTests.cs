using CleaningSuite.Application.Invoicing;
using CleaningSuite.Domain.Companies;

namespace CleaningSuite.Tests.Invoicing;

public class PropertyPricePlansTests
{
    [Fact]
    public void The_earliest_property_price_also_prices_visits_dated_before_it()
    {
        var plans = PropertyPricePlans.From([new(new DateOnly(2026, 10, 1), "PerOccurrence", 40m, 25.5m, 60)]);

        var plan = Assert.Single(plans);
        Assert.Equal(new DateOnly(2000, 1, 1), plan.EffectiveFrom);
        Assert.Equal(40m, plan.RateNet);
        Assert.Equal(25.5m, plan.VatRatePercent);
    }

    [Fact]
    public void Later_property_prices_start_on_their_own_dates()
    {
        var plans = PropertyPricePlans.From([
            new(new DateOnly(2026, 10, 1), "PerOccurrence", 40m, 25.5m, 60),
            new(new DateOnly(2026, 12, 1), "PerOccurrence", 45m, 25.5m, 60)]);

        Assert.Equal([new DateOnly(2000, 1, 1), new DateOnly(2026, 12, 1)], plans.Select(p => p.EffectiveFrom).ToArray());
    }
}
