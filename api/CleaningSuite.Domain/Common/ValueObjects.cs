using System.Globalization;

namespace CleaningSuite.Domain.Common;

/// <summary>Localized text per language, fi/en/sv keys.</summary>
public class LocalizedText
{
    public Dictionary<string, string> Values { get; set; } = new();

    public string? For(string lang) =>
        Values.TryGetValue(lang, out var v) ? v : (Values.TryGetValue("fi", out var f) ? f : null);
}

/// <summary>Street address.</summary>
public class Address
{
    public string Street { get; set; } = "";
    public string PostalCode { get; set; } = "";
    public string City { get; set; } = "";
    public string? Country { get; set; }
}

/// <summary>Money with per-line VAT math. All amounts are decimal.</summary>
public class Money
{
    /// <summary>Currency symbol used by <see cref="Summary"/> by default.</summary>
    public const string DefaultCurrencySymbol = "€";

    /// <summary>
    /// Single rounding policy for money: 2 decimals, midpoint-to-even (the default of
    /// <see cref="decimal.Round(decimal, int)"/>). Applied by <see cref="FromNet"/> when
    /// constructing amounts and by the formatting helpers below when rendering them.
    /// <see cref="Gross"/> is authoritative for stored and downstream math; <see cref="Summary"/>
    /// is presentation-only and rounds for display, so values assigned directly to <see cref="Net"/>
    /// or <see cref="Vat"/> with more than 2 decimals are not normalised here and may therefore
    /// differ from the displayed amount.
    /// </summary>
    private const MidpointRounding RoundingMode = MidpointRounding.ToEven;

    private static readonly NumberFormatInfo CurrencyFormat = NumberFormatInfo.ReadOnly(new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = "\u00A0",
        NegativeSign = "-",
    });

    public decimal Net { get; set; }
    public decimal Vat { get; set; }
    public decimal Gross => Net + Vat;

    /// <summary>
    /// Formatted gross total, e.g. "123,45 €". Always uses a comma decimal separator and a
    /// non-breaking space (U+00A0) group separator, independent of the current culture.
    /// Presentation-only; keep it out of the API wire contract via serializer/DTO configuration.
    /// </summary>
    public string Summary => SummaryFor(DefaultCurrencySymbol);

    /// <summary>
    /// Formatted gross total with the supplied currency symbol, e.g. "123,45 €".
    /// The symbol is trimmed, and the amount and symbol are joined by a non-breaking space
    /// (U+00A0) so they never wrap onto separate lines.
    /// A null/blank/whitespace symbol yields just the number, e.g. "123,45".
    /// </summary>
    public string SummaryFor(string? currencySymbol)
    {
        var gross = decimal.Round(Gross, 2, RoundingMode);
        var text = gross.ToString("N2", CurrencyFormat);
        var symbol = currencySymbol?.Trim();
        return string.IsNullOrEmpty(symbol) ? text : $"{text}\u00A0{symbol}";
    }

    public static Money FromNet(decimal net, decimal vatRatePercent) =>
        new()
        {
            Net = decimal.Round(net, 2, RoundingMode),
            Vat = decimal.Round(net * vatRatePercent / 100m, 2, RoundingMode),
        };
}

/// <summary>Start/end time pair for a weekday working window. Null end means closed.</summary>
public class WorkHours
{
    public TimeSpan? Start { get; set; }
    public TimeSpan? End { get; set; }
}
