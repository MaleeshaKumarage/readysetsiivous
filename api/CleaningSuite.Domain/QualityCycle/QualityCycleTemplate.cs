using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.QualityCycle;

public class QualityCycleTemplate : BaseDocument
{
    public Guid? CompanyId { get; set; }
    public Guid? BranchId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string> Items { get; set; } = new();
    public bool IsActive { get; set; } = true;

    public static QualityCycleTemplate Create(
        string title,
        List<string> items,
        string? description = null,
        Guid? companyId = null,
        Guid? branchId = null)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title is mandatory.", nameof(title));
        if (items is null || items.Count == 0)
            throw new ArgumentException("At least one item is required for a checklist template.", nameof(items));

        var cleanedItems = items
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Select(i => i.Trim())
            .ToList();

        if (cleanedItems.Count == 0)
            throw new ArgumentException("At least one non-empty item is required.", nameof(items));

        return new QualityCycleTemplate
        {
            Id = Guid.NewGuid(),
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
            Title = title.Trim(),
            Description = description?.Trim(),
            Items = cleanedItems,
            CompanyId = companyId,
            BranchId = branchId,
            IsActive = true
        };
    }

    public void Update(
        string title,
        List<string> items,
        string? description = null,
        bool isActive = true,
        Guid? companyId = null,
        Guid? branchId = null)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title is mandatory.", nameof(title));
        if (items is null || items.Count == 0)
            throw new ArgumentException("At least one item is required for a checklist template.", nameof(items));

        var cleanedItems = items
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Select(i => i.Trim())
            .ToList();

        if (cleanedItems.Count == 0)
            throw new ArgumentException("At least one non-empty item is required.", nameof(items));

        Title = title.Trim();
        Description = description?.Trim();
        Items = cleanedItems;
        IsActive = isActive;
        CompanyId = companyId;
        BranchId = branchId;
        UpdatedUtc = DateTime.UtcNow;
    }
}
