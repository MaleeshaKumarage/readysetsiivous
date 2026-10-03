using System.Text.Json.Serialization;

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

    /// <summary>
    /// Derived gross amount (<see cref="Net"/> + <see cref="Vat"/>). Excluded from serialization
    /// because it is computed from already-serialized members; emitting it would silently alter
    /// API payloads and generated clients.
    /// </summary>
    [JsonIgnore]
    public decimal Gross => Net + Vat;

    /// <summary>
    /// Builds a <see cref="Money"/> from a net amount and a VAT rate percentage. Both <see cref="Net"/>
    /// and <see cref="Vat"/> are rounded to 2 decimals (cents) using <see cref="MidpointRounding.AwayFromZero"/>.
    /// This is deliberate: the default <see cref="MidpointRounding.ToEven"/> (banker's) rounding would make a
    /// computed gross of e.g. 12.345 render as "12,34" instead of the expected "12,35". Rounding here (rather
    /// than relying on the formatter) guarantees that <see cref="Gross"/> already lands on an exact cent value
    /// and therefore renders identically regardless of the culture/ICU <c>CurrencyDecimalDigits</c> in effect.
    /// </summary>
    public static Money FromNet(decimal net, decimal vatRatePercent) =>
        new()
        {
            Net = decimal.Round(net, 2, MidpointRounding.AwayFromZero),
            Vat = decimal.Round(net * vatRatePercent / 100m, 2, MidpointRounding.AwayFromZero),
        };
}

/// <summary>Start/end time pair for a weekday working window. Null end means closed.</summary>
public class WorkHours
{
    public TimeSpan? Start { get; set; }
    public TimeSpan? End { get; set; }
}
