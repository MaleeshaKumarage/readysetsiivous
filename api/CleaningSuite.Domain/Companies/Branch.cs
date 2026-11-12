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

    /// <summary>
    /// Start date shown for a branch's old flat price, and for every price the tenant records from then on as history.
    /// </summary>
    public static readonly DateOnly LegacyPriceStart = new(2026, 10, 1);

    /// <summary>
    /// Date billing stores for the old flat price. Shifts billed under it keep this date, so it is read back as the
    /// earliest version and never rewritten in storage.
    /// </summary>
    public static readonly DateOnly LegacyStoredDate = new(2000, 1, 1);

    /// <summary>All price versions, oldest first. A pre-history branch's flat price starts on <see cref="LegacyPriceStart"/>.</summary>
    public IReadOnlyList<BranchPriceVersion> Versions => Present(StoredVersions);

    private IReadOnlyList<BranchPriceVersion> StoredVersions =>
        PriceVersions.Count > 0 ? PriceVersions.OrderBy(v => v.EffectiveFrom).ToArray()
        : PriceNet is { } legacy ? [new BranchPriceVersion(LegacyStoredDate, "PerOccurrence", legacy, VatRatePercent ?? 0, EstimatedDurationMinutes ?? 0)]
        : [];

    private static IReadOnlyList<BranchPriceVersion> Present(IReadOnlyList<BranchPriceVersion> stored) =>
        stored.Select(v => v.EffectiveFrom == LegacyStoredDate ? v with { EffectiveFrom = LegacyPriceStart } : v)
            .OrderBy(v => v.EffectiveFrom).ToArray();

    /// <summary>
    /// The version a billing date points at. Accepts the date as shown, or the stored date a shift billed under the
    /// old flat price keeps (2000-01-01), which resolves to that same version.
    /// </summary>
    public BranchPriceVersion? VersionFrom(DateOnly date) =>
        Versions.FirstOrDefault(v => v.EffectiveFrom == date)
        ?? Present(StoredVersions.Where(v => v.EffectiveFrom == date).ToArray()).FirstOrDefault();

    /// <summary>The version in force on a date: the latest one starting on or before it.</summary>
    public BranchPriceVersion? PriceOn(DateOnly date) => Versions.LastOrDefault(v => v.EffectiveFrom <= date);

    /// <summary>The version in force today (UTC date).</summary>
    public BranchPriceVersion? CurrentPrice => PriceOn(DateOnly.FromDateTime(DateTime.UtcNow));
    public decimal? CurrentPriceNet => CurrentPrice?.PriceNet;
    public decimal? CurrentVatRatePercent => CurrentPrice?.VatRatePercent;
    public int? CurrentEstimatedDurationMinutes => CurrentPrice?.DurationMinutes;

    public bool HasPendingQuote => QuoteRequestedUtc.HasValue && !PricingReady;
    public bool PricingReady => CurrentPrice is { PriceNet: > 0, VatRatePercent: >= 0 and <= 100, DurationMinutes: > 0 and <= 1440 };

    /// <summary>Validates and sets only the size, so a cleaning type stored before validation existed is left alone.</summary>
    public void SetSizeM2(int? sizeM2)
    {
        if (sizeM2 is < 1 or > 100000)
            throw new ArgumentException("Size must be between 1 and 100000 m², or empty.");
        SizeM2 = sizeM2;
    }

    /// <summary>Whether the size needed to schedule a shift for this property is set. Cleaning type is per visit.</summary>
    public bool HasNameDetails => SizeM2 is > 0;

    public void SetPropertyCounts(int? rooms, int? beds)
    {
        if (rooms is < 0 or > 10000 || beds is < 0 or > 10000)
            throw new ArgumentException("Rooms and beds must be between zero and 10000, or empty.");
        RoomCount = rooms; BedCount = beds;
    }

    /// <summary>
    /// Records a price effective from a date. A new date adds a version to the history. Correcting the price already
    /// recorded for a date replaces that version in place, as the branch price always has; a new price from a later
    /// date keeps the earlier history. Returns false when the price already in force is re-saved unchanged.
    /// </summary>
    public bool SetPricing(decimal priceNet, decimal vatRatePercent, int durationMinutes,
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
        var versions = StoredVersions.ToList();
        var wanted = new BranchPriceVersion(date, mode, priceNet, vatRatePercent, durationMinutes);
        // Re-saving the price already in force (for example an unchanged form) adds nothing to history.
        if (PriceOn(date) is { } inForce && inForce with { EffectiveFrom = date } == wanted) return false;
        // The first price on this branch starts on LegacyPriceStart, so a new price cannot be dated before it or on it.
        if (StoredVersions.Any(v => v.EffectiveFrom == LegacyStoredDate) && date <= LegacyPriceStart)
            throw new ArgumentException("Prices start on 01/10/2026. Choose a later effective date for a new price.");
        versions.RemoveAll(v => v.EffectiveFrom == date);
        versions.Add(wanted);
        PriceVersions = versions.OrderBy(v => v.EffectiveFrom).ToList();
        PriceNet = null; VatRatePercent = null; EstimatedDurationMinutes = null;
        PricingVersion++;
        QuoteRequestedUtc = null; QuoteRejectedUtc = null; QuoteRejectionReason = null; UpdatedUtc = DateTime.UtcNow;
        return true;
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
    /// <summary>Net, excl. VAT, added to each completed Deep visit on invoices.</summary>
    public const decimal DeepSurchargeNet = 15m;

    /// <summary>True for Deep or Normal. Null is not a known type.</summary>
    public static bool IsKnown(string? value) => value is Deep or Normal;

    /// <summary>A missing type is Normal. Anything other than Deep or Normal is refused.</summary>
    public static string Normalize(string? value) => string.IsNullOrEmpty(value) ? Normal
        : IsKnown(value) ? value!
        : throw new ArgumentException("Cleaning type must be Deep or Normal.");
}
