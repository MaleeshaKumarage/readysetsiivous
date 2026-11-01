using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Companies;

public class Branch : BaseDocument
{
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Street { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
    public bool IsActive { get; set; }
    public int? RoomCount { get; set; }
    public int? BedCount { get; set; }
    public decimal? PriceNet { get; set; }
    public decimal? VatRatePercent { get; set; }
    public int? EstimatedDurationMinutes { get; set; }
    public int PricingVersion { get; set; }
    public DateTime? QuoteRequestedUtc { get; set; }
    public bool PricingReady => PriceNet is > 0 && VatRatePercent is >= 0 and <= 100 && EstimatedDurationMinutes is > 0 and <= 1440;

    public void SetPropertyCounts(int? rooms, int? beds)
    {
        if (rooms is < 0 or > 10000 || beds is < 0 or > 10000)
            throw new ArgumentException("Rooms and beds must be between zero and 10000, or empty.");
        RoomCount = rooms; BedCount = beds;
    }

    public void SetPricing(decimal priceNet, decimal vatRatePercent, int durationMinutes)
    {
        if (priceNet <= 0 || priceNet > 1000000 || decimal.Round(priceNet, 2) != priceNet)
            throw new ArgumentException("Enter a positive net cleaning price with at most two decimal places.");
        if (vatRatePercent < 0 || vatRatePercent > 100 || decimal.Round(vatRatePercent, 3) != vatRatePercent)
            throw new ArgumentException("Enter a VAT rate between zero and 100.");
        if (durationMinutes < 1 || durationMinutes > 1440)
            throw new ArgumentException("Estimated cleaning duration must be between 1 and 1440 minutes.");
        if (PriceNet != priceNet || VatRatePercent != vatRatePercent || EstimatedDurationMinutes != durationMinutes)
            PricingVersion++;
        PriceNet = priceNet; VatRatePercent = vatRatePercent; EstimatedDurationMinutes = durationMinutes;
        QuoteRequestedUtc = null; UpdatedUtc = DateTime.UtcNow;
    }

    public static Branch Create(
        Guid companyId,
        string name,
        string? street = null,
        string? postalCode = null,
        string? city = null,
        string? country = null,
        string? contactPhone = null)
    {
        if (companyId == Guid.Empty)
            throw new ArgumentException("Company id is mandatory.", nameof(companyId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Branch name is mandatory.", nameof(name));

        return new Branch
        {
            Id = Guid.NewGuid(),
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
            CompanyId = companyId,
            Name = name.Trim(),
            Street = street?.Trim(),
            PostalCode = postalCode?.Trim(),
            City = city?.Trim(),
            Country = country?.Trim(),
            ContactPhone = contactPhone?.Trim(),
            IsActive = true
        };
    }

    public void Update(
        string name,
        string? street,
        string? postalCode,
        string? city,
        string? country,
        string? contactPhone,
        bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Branch name is mandatory.", nameof(name));

        Name = name.Trim();
        Street = street?.Trim();
        PostalCode = postalCode?.Trim();
        City = city?.Trim();
        Country = country?.Trim();
        ContactPhone = contactPhone?.Trim();
        IsActive = isActive;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedUtc = DateTime.UtcNow;
    }
}
