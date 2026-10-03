using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Serialization;

namespace CleaningSuite.Domain.Common;

/// <summary>A monetary amount in a given currency.</summary>
public class Money
{
    /// <summary>Net (pre-VAT) amount.</summary>
    public decimal Net { get; set; }

    /// <summary>VAT rate applied, as a percentage (e.g. 24 for 24%).</summary>
    public decimal VatRatePercent { get; set; }

    /// <summary>Computed VAT amount.</summary>
    public decimal VatAmount { get; set; }

    /// <summary>VAT amount charged.</summary>
    public decimal Vat { get; set; }

    /// <summary>Gross (VAT-inclusive) amount.</summary>
    public decimal Gross { get; set; }

    /// <summary>ISO 4217 currency code.</summary>
    public string Currency { get; set; } = "EUR";

    /// <summary>Creates an amount from a net value and a VAT rate, rounding VAT to cents.</summary>
    public static Money FromNet(decimal net, decimal vatRatePercent, string currency = "EUR")
    {
        var vat = Math.Round(net * vatRatePercent / 100m, 2, MidpointRounding.AwayFromZero);
        return new Money
        {
            Net = net,
            VatRatePercent = vatRatePercent,
            VatAmount = vat,
            Vat = vat,
            Gross = net + vat,
            Currency = currency,
        };
    }

    /// <summary>Gross amount with its currency symbol. Culture independent; not serialized.</summary>
    [JsonIgnore]
    public string Summary => $"{Format(Gross)}\u00A0{CurrencySymbol}";

    /// <summary>
    /// Formats the gross amount using a comma decimal separator and a space group separator.
    /// A non-blank <paramref name="symbol"/> is appended after a space; otherwise the number alone is returned.
    /// </summary>
    public string SummaryFor(string? symbol)
    {
        var number = Format(Gross);
        return string.IsNullOrWhiteSpace(symbol) ? number : $"{number} {symbol}";
    }

    private string CurrencySymbol => Currency switch
    {
        "EUR" => "€",
        _ => Currency,
    };

    private static readonly NumberFormatInfo NumberFormat = CreateNumberFormat();

    private static NumberFormatInfo CreateNumberFormat()
    {
        var format = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        format.NumberDecimalSeparator = ",";
        format.NumberGroupSeparator = " ";
        format.NumberGroupSizes = new[] { 3 };
        return format;
    }

    private static string Format(decimal value) => value.ToString("N2", NumberFormat);
}

/// <summary>Postal address.</summary>
public class Address
{
    public string Street { get; set; } = "";
    public string PostalCode { get; set; } = "";
    public string City { get; set; } = "";
    public string Country { get; set; } = "";
}

/// <summary>Text in each supported site language.</summary>
public class LocalizedText
{
    public string Fi { get; set; } = "";
    public string En { get; set; } = "";
    public string Sv { get; set; } = "";

    /// <summary>
    /// Language-keyed view of the localized values. Setting replaces all languages.
    /// Not serialized; the individual language properties are persisted.
    /// </summary>
    [JsonIgnore]
    public Dictionary<string, string> Values
    {
        get => new()
        {
            ["fi"] = Fi,
            ["en"] = En,
            ["sv"] = Sv,
        };
        set
        {
            Fi = value.TryGetValue("fi", out var fi) ? fi : "";
            En = value.TryGetValue("en", out var en) ? en : "";
            Sv = value.TryGetValue("sv", out var sv) ? sv : "";
        }
    }

    /// <summary>Returns the value for the given language code, or null when absent or blank.</summary>
    public string? For(string lang)
    {
        var value = lang switch
        {
            "fi" => Fi,
            "en" => En,
            "sv" => Sv,
            _ => null,
        };
        return string.IsNullOrEmpty(value) ? null : value;
    }
}

/// <summary>Start/end time pair for a weekday working window. Null end means closed.</summary>
public class WorkHours
{
    public TimeSpan? Start { get; set; }
    public TimeSpan? End { get; set; }

    /// <summary>True when this window is closed (no end time).</summary>
    public bool IsClosed => End is null;
}
