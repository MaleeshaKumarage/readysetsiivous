using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Companies;

public record BranchPriceVersion(DateOnly EffectiveFrom, string Mode, decimal PriceNet, decimal VatRatePercent, int DurationMinutes);

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
    /// <summary>Size in square metres, used in every shift name. Required before a shift can be scheduled.</summary>
    public int? SizeM2 { get; set; }
    /// <summary>"Deep" or "Normal". Required before a shift can be scheduled.</summary>
    public string? CleaningType { get; set; }
    /// <summary>Dated price history. The only source of a branch price; shifts pick one version by its effective date.</summary>
    public List<BranchPriceVersion> PriceVersions { get; set; } = [];
    // Pre-history flat price, kept only so documents saved before PriceVersions still load. Read through Versions;
    // cleared as soon as the first version is recorded.
    public decimal? PriceNet { get; set; }
    public decimal? VatRatePercent { get; set; }
    public int? EstimatedDurationMinutes { get; set; }
    public int PricingVersion { get; set; }
    public DateTime? QuoteRequestedUtc { get; set; }
    public DateTime? QuoteRejectedUtc { get; set; }
    public string? QuoteRejectionReason { get; set; }

    /// <summary>All price versions, oldest first. A pre-history branch is presented as one version from 2000-01-01.</summary>
    public IReadOnlyList<BranchPriceVersion> Versions =>
        PriceVersions.Count > 0 ? PriceVersions.OrderBy(v => v.EffectiveFrom).ToArray()
        : PriceNet is { } legacy ? [new BranchPriceVersion(new DateOnly(2000, 1, 1), "PerOccurrence", legacy, VatRatePercent ?? 0, EstimatedDurationMinutes ?? 0)]
        : [];

    /// <summary>The version in force on a date: the latest one starting on or before it.</summary>
    public BranchPriceVersion? PriceOn(DateOnly date) => Versions.LastOrDefault(v => v.EffectiveFrom <= date);

    /// <summary>The version in force today (UTC date).</summary>
    public BranchPriceVersion? CurrentPrice => PriceOn(DateOnly.FromDateTime(DateTime.UtcNow));
    public decimal? CurrentPriceNet => CurrentPrice?.PriceNet;
    public decimal? CurrentVatRatePercent => CurrentPrice?.VatRatePercent;
    public int? CurrentEstimatedDurationMinutes => CurrentPrice?.DurationMinutes;

    public bool HasPendingQuote => QuoteRequestedUtc.HasValue && !PricingReady;
    public bool PricingReady => CurrentPrice is { PriceNet: > 0, VatRatePercent: >= 0 and <= 100, DurationMinutes: > 0 and <= 1440 };

    /// <summary>Size and cleaning type, needed before a shift can be scheduled for this property.</summary>
    public void SetPropertyDetails(int? sizeM2, string? cleaningType)
    {
        SetSizeM2(sizeM2);
        SetCleaningType(cleaningType);
    }

    /// <summary>Validates and sets only the size, so a cleaning type stored before validation existed is left alone.</summary>
    public void SetSizeM2(int? sizeM2)
    {
        if (sizeM2 is < 1 or > 100000)
            throw new ArgumentException("Size must be between 1 and 100000 m², or empty.");
        SizeM2 = sizeM2;
    }

    /// <summary>Validates and sets only the cleaning type.</summary>
    public void SetCleaningType(string? cleaningType)
    {
        if (cleaningType is not null && cleaningType != CleaningTypes.Deep && cleaningType != CleaningTypes.Normal)
            throw new ArgumentException("Cleaning type must be Deep or Normal.");
        CleaningType = cleaningType;
    }

    public bool HasNameDetails => SizeM2 is > 0 && CleaningType is CleaningTypes.Deep or CleaningTypes.Normal;

    public void SetPropertyCounts(int? rooms, int? beds)
    {
        if (rooms is < 0 or > 10000 || beds is < 0 or > 10000)
            throw new ArgumentException("Rooms and beds must be between zero and 10000, or empty.");
        RoomCount = rooms; BedCount = beds;
    }

    /// <summary>
    /// Records a price effective from a date. A new date adds a version to the history. Correcting the price already
    /// recorded for a date replaces that version in place, as the branch price always has; a new price from a later
    /// date keeps the earlier history.
    /// </summary>
    public void SetPricing(decimal priceNet, decimal vatRatePercent, int durationMinutes,
        string mode = "PerOccurrence", DateOnly? effectiveFrom = null)
    {
        if (priceNet <= 0 || priceNet > 1000000 || decimal.Round(priceNet, 2) != priceNet)
            throw new ArgumentException("Enter a positive net cleaning price with at most two decimal places.");
        if (vatRatePercent < 0 || vatRatePercent > 100 || decimal.Round(vatRatePercent, 3) != vatRatePercent)
            throw new ArgumentException("Enter a VAT rate between zero and 100.");
        if (durationMinutes < 1 || durationMinutes > 1440)
            throw new ArgumentException("Estimated cleaning duration must be between 1 and 1440 minutes.");
        if (mode is not ("Monthly" or "Hourly" or "PerOccurrence"))
            throw new ArgumentException("Choose monthly, hourly or per-occurrence pricing.");
        var date = effectiveFrom ?? DateOnly.FromDateTime(DateTime.UtcNow);
        if (date.Year is < 2000 or > 2100) throw new ArgumentException("Enter a valid effective date between 2000 and 2100.");
        var versions = Versions.ToList();
        var wanted = new BranchPriceVersion(date, mode, priceNet, vatRatePercent, durationMinutes);
        // Re-saving the price already in force (for example an unchanged form) adds nothing to history.
        if (PriceOn(date) is { } inForce && inForce with { EffectiveFrom = date } == wanted) return;
        versions.RemoveAll(v => v.EffectiveFrom == date);
        versions.Add(wanted);
        PriceVersions = versions.OrderBy(v => v.EffectiveFrom).ToList();
        PriceNet = null; VatRatePercent = null; EstimatedDurationMinutes = null;
        PricingVersion++;
        QuoteRequestedUtc = null; QuoteRejectedUtc = null; QuoteRejectionReason = null; UpdatedUtc = DateTime.UtcNow;
    }

    /// <summary>The owner withdraws a pending quote request. Staff queues drop it because it is no longer requested.</summary>
    public void WithdrawQuote(DateTime nowUtc)
    {
        QuoteRequestedUtc = null; UpdatedUtc = nowUtc;
    }

    /// <summary>Staff decline a pending quote request with a reason the owner can read.</summary>
    public void RejectQuote(string reason, DateTime nowUtc)
    {
        QuoteRejectedUtc = nowUtc; QuoteRejectionReason = reason.Trim(); QuoteRequestedUtc = null; UpdatedUtc = nowUtc;
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

public static class CleaningTypes
{
    public const string Deep = "Deep";
    public const string Normal = "Normal";
}
