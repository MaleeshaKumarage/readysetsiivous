using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.QualityCycle;

public class QualityCycleFormItem
{
    public string ItemText { get; set; } = string.Empty;
    public bool IsChecked { get; set; }
}

public record QualityCyclePhoto(string Url, string Note);

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
    public List<QualityCyclePhoto> Photos { get; set; } = new();
    public string? CleanerNotes { get; set; }
    public bool IsSubmitted { get; set; }
    public DateTime? SubmittedUtc { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public DateTime? EmailSentUtc { get; set; }
    public DateTime? EmailLastAttemptUtc { get; set; }
    public string? EmailError { get; set; }
    public string TaskStatus => !IsSubmitted ? "Pending"
        : "Recorded";
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }

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

    public void Start(DateTime? startedAtUtc = null)
    {
        if (StartedAtUtc.HasValue) return;
        var now = AsUtc(startedAtUtc ?? DateTime.UtcNow);
        StartedAtUtc = now;
        UpdatedUtc = now;
    }

    public void Submit(List<QualityCycleFormItem> items, List<string>? photoUrls, string? cleanerNotes, List<QualityCyclePhoto>? photos = null, DateTime? endedAtUtc = null)
    {
        if (IsSubmitted)
            throw new InvalidOperationException("Quality Cycle Form has already been submitted.");

        if (items is null || items.Count != Items.Count
            || items.Where((item, i) => item.ItemText != Items[i].ItemText).Any())
            throw new ArgumentException("Submit the complete checklist in its original order.", nameof(items));

        var legacyUrls = photoUrls?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() ?? new List<string>();
        var notes = cleanerNotes?.Trim();
        if (legacyUrls.Count > 0)
            throw new ArgumentException("Add a separate note for each photo and submit paired photos rather than photoUrls.", nameof(photos));
        if (photos?.Any(p => p is null || string.IsNullOrWhiteSpace(p.Url) || string.IsNullOrWhiteSpace(p.Note)) == true)
            throw new ArgumentException("Add a separate note for each photo.", nameof(photos));
        var evidence = photos?.Select(p => new QualityCyclePhoto(p.Url.Trim(), p.Note.Trim())).ToList() ?? new();

        if (items.Count > 0)
        {
            // Preserve original template item texts and structure; only update IsChecked property
            for (int i = 0; i < Items.Count; i++)
            {
                Items[i].IsChecked = items[i].IsChecked;
            }
        }

        Photos = evidence;
        // New evidence stores each image once, paired with its note. PhotoUrls
        // remains for historical documents; DTOs derive the compatibility view.
        PhotoUrls = new();
        CleanerNotes = notes;
        IsSubmitted = true;
        var now = AsUtc(endedAtUtc ?? DateTime.UtcNow);
        SubmittedUtc = now;
        EndedAtUtc = now;
        UpdatedUtc = now;
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
