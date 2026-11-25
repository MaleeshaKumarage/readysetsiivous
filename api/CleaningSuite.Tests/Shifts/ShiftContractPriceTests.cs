using CleaningSuite.Domain.Shifts;
using Xunit;

namespace CleaningSuite.Tests.Shifts;

public sealed class ShiftContractPriceTests
{
    private static Shift NewShift() => new() { Id = Guid.NewGuid(), Name = "Weekly clean" };

    [Fact]
    public void A_monthly_price_is_stored_on_the_shift()
    {
        var shift = NewShift();
        shift.SetContractPrice(ShiftPricingMode.Monthly, 120m, 25.5m);

        Assert.Equal(ShiftPricingMode.Monthly, shift.PricingMode);
        Assert.Equal(120m, shift.RateNet);
        Assert.Equal(25.5m, shift.VatRatePercent);
    }

    [Fact]
    public void Clearing_all_three_values_returns_the_shift_to_the_branch_price()
    {
        var shift = NewShift();
        shift.SetContractPrice(ShiftPricingMode.PerVisit, 40m, 25.5m);
        shift.SetContractPrice(null, null, null);

        Assert.Null(shift.PricingMode);
        Assert.Null(shift.RateNet);
        Assert.Null(shift.VatRatePercent);
    }

    [Theory]
    [InlineData(null, 40.0, 25.5)]
    [InlineData(ShiftPricingMode.Monthly, null, 25.5)]
    [InlineData(ShiftPricingMode.Monthly, 40.0, null)]
    public void A_partial_price_is_refused(ShiftPricingMode? mode, double? rate, double? vat)
    {
        var shift = NewShift();
        Assert.Throws<ArgumentException>(() => shift.SetContractPrice(mode, rate.HasValue ? (decimal)rate.Value : null, vat.HasValue ? (decimal)vat.Value : null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_zero_or_negative_rate_is_refused(double rate)
    {
        var shift = NewShift();
        Assert.Throws<ArgumentException>(() => shift.SetContractPrice(ShiftPricingMode.PerVisit, (decimal)rate, 25.5m));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void VAT_outside_0_to_100_is_refused(double vat)
    {
        var shift = NewShift();
        Assert.Throws<ArgumentException>(() => shift.SetContractPrice(ShiftPricingMode.Monthly, 120m, (decimal)vat));
    }
}
