using CleaningSuite.Domain.Common;

namespace CleaningSuite.Domain.Companies;

public class Branch : BaseDocument
{
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Street { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? ContactPhone { get; set; }
    public bool IsActive { get; set; }

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
