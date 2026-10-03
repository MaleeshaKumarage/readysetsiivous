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
    public decimal Net { get; set; }
    public decimal Vat { get; set; }
    public decimal Gross => Net + Vat;

    /// <summary>
    /// Formats the gross amount (<see cref="Net"/> + <see cref="Vat"/>) as a currency string using the
    /// supplied culture, e.g. "123,45 €" for fi-FI or "€123.45" for en-US. Currency formatting is a
    /// presentation concern, so the caller in the presentation/reporting layer chooses the culture.
    /// </summary>
    public string Format(IFormatProvider? provider) => Gross.ToString("C", provider);

    public static Money FromNet(decimal net, decimal vatRatePercent) =>
        new()
        {
            Net = decimal.Round(net, 2),
            Vat = decimal.Round(net * vatRatePercent / 100m, 2),
        };
}

/// <summary>Start/end time pair for a weekday working window. Null end means closed.</summary>
public class WorkHours
{
    public TimeSpan? Start { get; set; }
    public TimeSpan? End { get; set; }
}
