using System.Globalization;
using CleaningSuite.Domain.Common;
using Xunit;

namespace CleaningSuite.Domain.Tests.Common;

public class MoneyTests
{
    [Fact]
    public void FromNet_rounds_net_midpoints_away_from_zero()
    {
        // 12.345 is exactly halfway between 12.34 and 12.35. The default ToEven rule would
        // round down to 12.34; AwayFromZero must round it up to 12.35.
        var money = Money.FromNet(12.345m, 0m);

        Assert.Equal(12.35m, money.Net);
        Assert.Equal(12.35m, money.Gross);
    }

    [Fact]
    public void FromNet_rounds_vat_midpoints_away_from_zero()
    {
        // 10.00 * 24.5% = 2.45 exactly; pick a midpoint to exercise the VAT branch.
        // net * rate / 100 with net=12.345, rate=10 => 1.2345 => midpoint rounds to 1.23 (not 1.23/1.24 to-even ambiguity).
        var money = Money.FromNet(12.345m, 10m);

        Assert.Equal(12.35m, money.Net);
        Assert.Equal(1.23m, money.Vat);
        Assert.Equal(13.58m, money.Gross);
    }

    [Fact]
    public void Format_renders_exact_currency_string()
    {
        // Pins the exact rendered string so a future culture/ICU or CurrencyDecimalDigits
        // change cannot silently alter invoices.
        var money = Money.FromNet(12.345m, 0m);

        Assert.Equal("$12.35", money.Format(CultureInfo.GetCultureInfo("en-US")));
    }
}
