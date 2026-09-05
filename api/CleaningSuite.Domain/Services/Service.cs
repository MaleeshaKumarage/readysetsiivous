using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Services;

/// <summary>A bookable cleaning service with localized name, description and price.</summary>
public class Service : BaseDocument
{
    public string Slug { get; set; } = "";
    public string Category { get; set; } = "";
    public LocalizedText Name { get; set; } = new();
    public LocalizedText Description { get; set; } = new();
    public int DurationMinutes { get; set; }
    public decimal PriceNet { get; set; }
    public decimal VatRatePercent { get; set; }
    public string Currency { get; set; } = "EUR";
    public bool IsActive { get; set; } = true;
    public bool IsFeatured { get; set; }
    public int SortOrder { get; set; }
}
