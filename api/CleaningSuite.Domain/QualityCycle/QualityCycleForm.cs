using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.QualityCycle;

public class QualityCycleFormItem
{
    public string ItemText { get; set; } = string.Empty;
    public bool IsChecked { get; set; }
}

public class QualityCycleForm : BaseDocument
{
    public Guid ShiftId { get; set; }
    public string ShiftName { get; set; } = string.Empty;
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public Guid TemplateId { get; set; }
    public string TemplateTitle { get; set; } = string.Empty;
    public DateTime ShiftOccurrenceUtc { get; set; }
    public DateTime ShiftOccurrenceEndUtc { get; set; }
    public string Token { get; set; } = string.Empty;
    public List<QualityCycleFormItem> Items { get; set; } = new();
    public List<string> PhotoUrls { get; set; } = new();
    public string? CleanerNotes { get; set; }
    public bool IsSubmitted { get; set; }
    public DateTime? SubmittedUtc { get; set; }

    public static QualityCycleForm Create(
        Guid shiftId,
        string shiftName,
        Guid employeeId,
        string employeeName,
        Guid templateId,
        string templateTitle,
        DateTime shiftOccurrenceUtc,
        List<string> templateItems,
        DateTime shiftOccurrenceEndUtc = default)
    {
        if (shiftId == Guid.Empty)
            throw new ArgumentException("ShiftId is mandatory.", nameof(shiftId));
        if (employeeId == Guid.Empty)
            throw new ArgumentException("EmployeeId is mandatory.", nameof(employeeId));

        var formItems = (templateItems ?? new List<string>())
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Select(i => new QualityCycleFormItem { ItemText = i.Trim(), IsChecked = false })
            .ToList();

        return new QualityCycleForm
        {
            Id = Guid.NewGuid(),
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
            ShiftId = shiftId,
            ShiftName = shiftName?.Trim() ?? string.Empty,
            EmployeeId = employeeId,
            EmployeeName = employeeName?.Trim() ?? string.Empty,
            TemplateId = templateId,
            TemplateTitle = templateTitle?.Trim() ?? string.Empty,
            ShiftOccurrenceUtc = shiftOccurrenceUtc,
            ShiftOccurrenceEndUtc = shiftOccurrenceEndUtc == default ? shiftOccurrenceUtc : shiftOccurrenceEndUtc,
            Token = Guid.NewGuid().ToString("N"),
            Items = formItems,
            PhotoUrls = new List<string>(),
            IsSubmitted = false,
            SubmittedUtc = null
        };
    }

    public void Submit(List<QualityCycleFormItem> items, List<string>? photoUrls, string? cleanerNotes)
    {
        if (IsSubmitted)
            throw new InvalidOperationException("Quality Cycle Form has already been submitted.");

        if (items != null && items.Count > 0)
        {
            // Preserve original template item texts and structure; only update IsChecked property
            for (int i = 0; i < Items.Count; i++)
            {
                var submittedMatch = items.FirstOrDefault(submitted =>
                    string.Equals(submitted.ItemText?.Trim(), Items[i].ItemText?.Trim(), StringComparison.OrdinalIgnoreCase));

                if (submittedMatch != null)
                {
                    Items[i].IsChecked = submittedMatch.IsChecked;
                }
                else if (i < items.Count)
                {
                    // Fallback to index-based matching if item text modified on client
                    Items[i].IsChecked = items[i].IsChecked;
                }
            }
        }

        PhotoUrls = photoUrls?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() ?? new List<string>();
        CleanerNotes = cleanerNotes?.Trim();
        IsSubmitted = true;
        SubmittedUtc = DateTime.UtcNow;
        UpdatedUtc = DateTime.UtcNow;
    }
}
