using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Leads;

public class LeadContactPerson
{
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Role { get; set; }
}

/// <summary>
/// Sales / pipeline lead. Supports a main contact plus extra emails and contact persons.
/// </summary>
public class Lead : BaseDocument
{
    public string Name { get; set; } = string.Empty;
    public string? BusinessId { get; set; }
    public string? MainContactName { get; set; }
    public string? MainContactEmail { get; set; }
    public string? MainContactPhone { get; set; }
    public List<string> Emails { get; set; } = new();
    public List<LeadContactPerson> ContactPersons { get; set; } = new();
    public string? Notes { get; set; }
    public bool IsActive { get; set; }

    public static Lead Create(
        string name,
        string? businessId = null,
        string? mainContactName = null,
        string? mainContactEmail = null,
        string? mainContactPhone = null,
        IEnumerable<string>? emails = null,
        IEnumerable<LeadContactPerson>? contactPersons = null,
        string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is mandatory.", nameof(name));

        return new Lead
        {
            Id = Guid.NewGuid(),
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
            Name = name.Trim(),
            BusinessId = string.IsNullOrWhiteSpace(businessId) ? null : businessId.Trim(),
            MainContactName = string.IsNullOrWhiteSpace(mainContactName) ? null : mainContactName.Trim(),
            MainContactEmail = string.IsNullOrWhiteSpace(mainContactEmail) ? null : mainContactEmail.Trim(),
            MainContactPhone = string.IsNullOrWhiteSpace(mainContactPhone) ? null : mainContactPhone.Trim(),
            Emails = NormalizeEmails(emails),
            ContactPersons = NormalizePeople(contactPersons),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            IsActive = true
        };
    }

    public void Update(
        string name,
        string? businessId,
        string? mainContactName,
        string? mainContactEmail,
        string? mainContactPhone,
        IEnumerable<string>? emails,
        IEnumerable<LeadContactPerson>? contactPersons,
        string? notes,
        bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is mandatory.", nameof(name));

        Name = name.Trim();
        BusinessId = string.IsNullOrWhiteSpace(businessId) ? null : businessId.Trim();
        MainContactName = string.IsNullOrWhiteSpace(mainContactName) ? null : mainContactName.Trim();
        MainContactEmail = string.IsNullOrWhiteSpace(mainContactEmail) ? null : mainContactEmail.Trim();
        MainContactPhone = string.IsNullOrWhiteSpace(mainContactPhone) ? null : mainContactPhone.Trim();
        Emails = NormalizeEmails(emails);
        ContactPersons = NormalizePeople(contactPersons);
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        IsActive = isActive;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void Reactivate()
    {
        IsActive = true;
        UpdatedUtc = DateTime.UtcNow;
    }

    private static List<string> NormalizeEmails(IEnumerable<string>? emails) =>
        (emails ?? Array.Empty<string>())
            .Select(e => e?.Trim())
            .Where(e => !string.IsNullOrEmpty(e))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();

    private static List<LeadContactPerson> NormalizePeople(IEnumerable<LeadContactPerson>? people) =>
        (people ?? Array.Empty<LeadContactPerson>())
            .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => new LeadContactPerson
            {
                Name = p.Name.Trim(),
                Email = string.IsNullOrWhiteSpace(p.Email) ? null : p.Email.Trim(),
                Phone = string.IsNullOrWhiteSpace(p.Phone) ? null : p.Phone.Trim(),
                Role = string.IsNullOrWhiteSpace(p.Role) ? null : p.Role.Trim()
            })
            .ToList();
}
