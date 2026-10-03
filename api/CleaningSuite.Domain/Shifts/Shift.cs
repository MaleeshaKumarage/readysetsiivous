using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Shifts;

public class Shift : BaseDocument
{
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public string Name { get; set; } = string.Empty;
    public ShiftSchedule Schedule { get; set; } = new();
    public string? Notes { get; set; }
    public bool IsActive { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }

    public static Shift Create(
        Guid companyId,
        Guid branchId,
        string name,
        ShiftSchedule schedule,
        string? notes,
        DateTime? validFrom,
        DateTime? validUntil)
    {
        if (companyId == Guid.Empty)
            throw new ArgumentException("Company is mandatory.", nameof(companyId));
        if (branchId == Guid.Empty)
            throw new ArgumentException("Branch is mandatory.", nameof(branchId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is mandatory.", nameof(name));
        if (schedule is null)
            throw new ArgumentNullException(nameof(schedule));

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
        DateTime? validUntil)
    {
        if (companyId == Guid.Empty)
            throw new ArgumentException("Company is mandatory.", nameof(companyId));
        if (branchId == Guid.Empty)
            throw new ArgumentException("Branch is mandatory.", nameof(branchId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is mandatory.", nameof(name));
        if (schedule is null)
            throw new ArgumentNullException(nameof(schedule));

        CompanyId = companyId;
        BranchId = branchId;
        Name = name.Trim();
        Schedule = schedule;
        Notes = notes?.Trim();
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
