using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Shifts;

public class Shift : BaseDocument
{
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public Guid? PartnerRequestId { get; set; }
    /// <summary>
    /// True for the one shift per branch that collects its on-call visits. Each partner booking or admin-added visit
    /// is an extra occurrence on it, not a shift of its own.
    /// </summary>
    public bool IsBranchMain { get; set; }
    /// <summary>The last visit number given out on a branch main shift. Numbers only go up and are never reused.</summary>
    public int OccurrenceCounter { get; set; }
    /// <summary>On a branch main shift: the employee each new visit is offered to. Null leaves new visits unassigned.</summary>
    public Guid? DefaultEmployeeId { get; set; }

    /// <summary>The id of a branch's main shift. Derived from the branch, so two writers can never create two mains.</summary>
    public static Guid BranchMainId(Guid branchId) => new(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes($"branch-main:{branchId:N}")).AsSpan(0, 16));

    public int NextOccurrenceSequence() => ++OccurrenceCounter;

    /// <summary>
    /// Widens a main shift's start so a visit on this local date is inside it. The first checklist plan follows,
    /// so a visit entered for an earlier date is still planned and billed.
    /// </summary>
    public void CoverVisitDate(DateTime localDate)
    {
        var date = localDate.Date;
        if (!ValidFrom.HasValue || ValidFrom.Value.Date > date) ValidFrom = date;
        var first = QualityCyclePlans.OrderBy(p => p.EffectiveFromUtc).FirstOrDefault();
        if (first is not null && (!first.ValidFrom.HasValue || first.ValidFrom.Value.Date > date)) first.ValidFrom = date;
    }
    public string Name { get; set; } = string.Empty;
    public ShiftSchedule Schedule { get; set; } = new();
    public string? Notes { get; set; }
    // Photo sections the employee must attach before ending a partner visit. Copied from the partner's settings at acceptance.
    public List<string> RequiredPhotoLabels { get; set; } = new();
    // Partner access and contact details, copied from the partner's settings at acceptance.
    public string? PartnerAccessInstructions { get; set; }
    public string? PartnerContactName { get; set; }
    public string? PartnerContactPhone { get; set; }
    public Guid? QualityCycleTemplateId { get; set; }
    /// <summary>How this shift is billed. Null on shifts created before contract pricing: they keep the branch price.</summary>
    public ShiftPricingMode? PricingMode { get; set; }
    /// <summary>Net price (excl. VAT). Monthly: the fee for each calendar month. PerVisit: the price of one completed visit.</summary>
    public decimal? RateNet { get; set; }
    /// <summary>VAT percentage for the shift's own price. Null with no price falls back to the branch price.</summary>
    public decimal? VatRatePercent { get; set; }
    public bool IsActive { get; set; }
    /// <summary>Stored list tab (active, completed or inactive). Kept current by ShiftTabs.StoreAsync so the list can page in the database.</summary>
    public string Tab { get; set; } = string.Empty;
    /// <summary>True when a one-time visit has a submitted checklist.</summary>
    public bool ChecklistCompleted { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
    public List<CleaningSuite.Domain.QualityCycle.QualityCyclePlan> QualityCyclePlans { get; set; } = new();

    /// <summary>
    /// A one-time visit is valid only on its selected date. Submitted validity dates are
    /// replaced so a stale hidden end date cannot cancel the visit.
    /// </summary>
    private static (DateTime? From, DateTime? Until) NormalizeValidity(
        ShiftSchedule schedule, DateTime? validFrom, DateTime? validUntil)
    {
        if (!schedule.IsOneTime || schedule.OneTimeDate is null) return (validFrom, validUntil);
        var date = schedule.OneTimeDate.Value.Date;
        return (date, date);
    }

    /// <summary>
    /// Sets the shift's own contract price. Pass all three values, or none to fall back to the branch price.
    /// </summary>
    public void SetContractPrice(ShiftPricingMode? mode, decimal? rateNet, decimal? vatRatePercent)
    {
        if (mode is null && rateNet is null && vatRatePercent is null)
        {
            PricingMode = null; RateNet = null; VatRatePercent = null;
            return;
        }
        if (mode is null || rateNet is null || vatRatePercent is null)
            throw new ArgumentException("A shift price needs its pricing mode, rate and VAT together.");
        if (rateNet <= 0) throw new ArgumentException("The shift price must be more than zero.", nameof(rateNet));
        if (vatRatePercent < 0 || vatRatePercent > 100) throw new ArgumentException("VAT must be between 0 and 100.", nameof(vatRatePercent));
        PricingMode = mode; RateNet = rateNet; VatRatePercent = vatRatePercent;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void RecordQualityCyclePlan(DateTime effectiveUtc)
    {
        QualityCyclePlans.Add(new()
        {
            EffectiveFromUtc = effectiveUtc, CompanyId = CompanyId, BranchId = BranchId,
            ShiftName = Name, TemplateId = QualityCycleTemplateId, IsActive = IsActive,
            ValidFrom = ValidFrom, ValidUntil = ValidUntil,
            Schedule = System.Text.Json.JsonSerializer.Deserialize<ShiftSchedule>(
                System.Text.Json.JsonSerializer.Serialize(Schedule))!
        });
    }

    public void EnsureQualityCycleHistory()
    {
        if (QualityCyclePlans.Count == 0 && QualityCycleTemplateId.HasValue)
            RecordQualityCyclePlan(CreatedUtc);
    }

    public static Shift Create(
        Guid companyId,
        Guid branchId,
        string name,
        ShiftSchedule schedule,
        string? notes,
        DateTime? validFrom,
        DateTime? validUntil,
        Guid? qualityCycleTemplateId = null)
    {
        if (companyId == Guid.Empty)
            throw new ArgumentException("Company is mandatory.", nameof(companyId));
        if (branchId == Guid.Empty)
            throw new ArgumentException("Branch is mandatory.", nameof(branchId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is mandatory.", nameof(name));
        if (schedule is null)
            throw new ArgumentNullException(nameof(schedule));
        schedule.Validate();
        (validFrom, validUntil) = NormalizeValidity(schedule, validFrom, validUntil);
        if (validFrom.HasValue && validUntil.HasValue && validUntil.Value < validFrom.Value)
            throw new ArgumentException("ValidUntil must be on or after ValidFrom.", nameof(validUntil));

        return new Shift
        {
            Id = Guid.NewGuid(),
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
            CompanyId = companyId,
            BranchId = branchId,
            Name = name.Trim(),
            Schedule = schedule,
            Notes = notes?.Trim(),
            QualityCycleTemplateId = qualityCycleTemplateId,
            IsActive = true,
            ValidFrom = validFrom,
            ValidUntil = validUntil
        };
    }

    public void Update(
        Guid companyId,
        Guid branchId,
        string name,
        ShiftSchedule schedule,
        string? notes,
        bool isActive,
        DateTime? validFrom,
        DateTime? validUntil,
        Guid? qualityCycleTemplateId = null)
    {
        if (companyId == Guid.Empty)
            throw new ArgumentException("Company is mandatory.", nameof(companyId));
        if (branchId == Guid.Empty)
            throw new ArgumentException("Branch is mandatory.", nameof(branchId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is mandatory.", nameof(name));
        if (schedule is null)
            throw new ArgumentNullException(nameof(schedule));
        schedule.Validate();
        (validFrom, validUntil) = NormalizeValidity(schedule, validFrom, validUntil);
        if (validFrom.HasValue && validUntil.HasValue && validUntil.Value < validFrom.Value)
            throw new ArgumentException("ValidUntil must be on or after ValidFrom.", nameof(validUntil));

        CompanyId = companyId;
        BranchId = branchId;
        Name = name.Trim();
        Schedule = schedule;
        Notes = notes?.Trim();
        QualityCycleTemplateId = qualityCycleTemplateId;
        IsActive = isActive;
        ValidFrom = validFrom;
        ValidUntil = validUntil;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedUtc = DateTime.UtcNow;
    }
}

/// <summary>How a shift's contract price is charged.</summary>
public enum ShiftPricingMode
{
    Monthly,
    PerVisit,
}
