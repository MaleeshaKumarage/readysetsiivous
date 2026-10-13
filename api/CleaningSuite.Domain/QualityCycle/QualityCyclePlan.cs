using CleaningSuite.Domain.Shifts;

namespace CleaningSuite.Domain.QualityCycle;

/// <summary>Effective-dated schedule snapshot; reports must not rewrite the past after editing a shift.</summary>
public class QualityCyclePlan
{
    public DateTime EffectiveFromUtc { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public string ShiftName { get; set; } = "";
    public ShiftSchedule Schedule { get; set; } = new();
    public Guid? TemplateId { get; set; }
    public bool IsActive { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
}
