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

    /// <summary>Lucide icon name rendered on the website card.</summary>
    public string Icon { get; set; } = "Sparkles";

    /// <summary>Uploaded image path (e.g. /uploads/abc.jpg) served by the API.</summary>
    public string ImageUrl { get; set; } = "";

    /// <summary>Optional extra info shown under the description.</summary>
    public LocalizedText AdditionalInfo { get; set; } = new();
}
