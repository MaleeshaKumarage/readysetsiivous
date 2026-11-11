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
    // Photo sections the cleaner must attach before ending a partner visit. Copied from the partner's settings at acceptance.
    public List<string> RequiredPhotoLabels { get; set; } = new();
    // Partner access and contact details, copied from the partner's settings at acceptance.
    public string? PartnerAccessInstructions { get; set; }
    public string? PartnerContactName { get; set; }
    public string? PartnerContactPhone { get; set; }
    public Guid? QualityCycleTemplateId { get; set; }
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
