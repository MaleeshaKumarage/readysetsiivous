using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Companies;

public class Company : BaseDocument
{
    public string BusinessId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; }

    public static Company Create(
        string businessId,
        string name,
        string? contactName = null,
        string? contactEmail = null,
        string? contactPhone = null,
        string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(businessId))
            throw new ArgumentException("Business ID is mandatory.", nameof(businessId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is mandatory.", nameof(name));

        return new Company
        {
            Id = Guid.NewGuid(),
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
            BusinessId = businessId.Trim(),
            Name = name.Trim(),
            ContactName = contactName?.Trim(),
            ContactEmail = contactEmail?.Trim(),
            ContactPhone = contactPhone?.Trim(),
            Notes = notes?.Trim(),
            IsActive = true
        };
    }

    public void Update(
        string businessId,
        string name,
        string? contactName,
        string? contactEmail,
        string? contactPhone,
        string? notes,
        bool isActive)
    {
        if (string.IsNullOrWhiteSpace(businessId))
            throw new ArgumentException("Business ID is mandatory.", nameof(businessId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is mandatory.", nameof(name));

        BusinessId = businessId.Trim();
        Name = name.Trim();
        ContactName = contactName?.Trim();
        ContactEmail = contactEmail?.Trim();
        ContactPhone = contactPhone?.Trim();
        Notes = notes?.Trim();
        IsActive = isActive;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedUtc = DateTime.UtcNow;
    }
}
