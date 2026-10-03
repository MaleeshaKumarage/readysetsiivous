using System.Globalization;
using CleaningSuite.Domain.Common;
using Xunit;

namespace CleaningSuite.Domain.Tests.Common;

/// <summary>
/// Locks in the presentation contract of <see cref="Money.Summary"/> and
/// <see cref="Money.SummaryFor(string)"/>: comma decimal separator, space group
/// separator, and independence from <see cref="CultureInfo.CurrentCulture"/>.
/// </summary>
public class MoneyFormattingTests
{
    [Fact]
    public void Summary_ZeroAmount_FormatsAsZeroWithTwoDecimals()
    {
        var money = Money.FromNet(0m, 24m);

        Assert.Equal(0m, money.Gross);
        Assert.Equal("0,00\u00A0€", money.Summary);
    }

    [Fact]
    public void Summary_NegativeAmount_PreservesNegativeSign()
    {
        var money = Money.FromNet(-10m, 24m);

        Assert.Equal(-12.40m, money.Gross);
        Assert.Equal("-12,40\u00A0€", money.Summary);
    }

    [Fact]
    public void SummaryFor_NullSymbol_YieldsNumberOnly()
    {
        var money = Money.FromNet(10m, 24m);

        Assert.Equal("12,40", money.SummaryFor(null));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    public void SummaryFor_WhitespaceSymbol_YieldsNumberOnly(string symbol)
    {
        var money = Money.FromNet(10m, 24m);

        Assert.Equal("12,40", money.SummaryFor(symbol));
    }

    [Fact]
    public void SummaryFor_CustomSymbol_AppendsSymbol()
    {
        var money = Money.FromNet(10m, 24m);

        Assert.Equal("12,40 $", money.SummaryFor("$"));
    }

    [Fact]
    public void Summary_IsCultureIndependent_CommaDecimalSpaceGroup()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            // de-DE uses a comma decimal separator and a *dot* group separator by
            // default. The formatter must ignore that and keep comma + space.
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var money = Money.FromNet(1000m, 24m);

            Assert.Equal("1\u0020240,00\u00A0€", money.Summary);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void SummaryFor_NullSymbol_IsCultureIndependent()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var money = Money.FromNet(1000m, 24m);

            Assert.Equal("1\u0020240,00", money.SummaryFor(null));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
