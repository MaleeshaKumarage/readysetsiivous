using CleaningSuite.Domain.Common;
namespace CleaningSuite.Domain.Leads;

public class Lead : BaseDocument
{
    public LeadFields Fields { get; set; } = new();
    public bool IsActive { get; set; } = true;
    public Guid? CompanyId { get; set; }
    public Guid? BranchId { get; set; }
    public DateTime? ConvertedUtc { get; set; }
    public List<LeadImage> Images { get; set; } = [];
}
public record LeadFields
{
    public string Name { get; init; } = "";
    public string BusinessId { get; init; } = "";
    public string ContactName { get; init; } = "";
    public string ContactPhone { get; init; } = "";
    public string Notes { get; init; } = "";
    public string Description { get; init; } = "";
    public List<string> Emails { get; init; } = [];
    public string PrimaryEmail { get; init; } = "";
    public string Status { get; init; } = "New";
    public string BranchName { get; init; } = "";
    public string Street { get; init; } = "";
    public string PostalCode { get; init; } = "";
    public string City { get; init; } = "";
    public string Country { get; init; } = "";
    public string BranchPhone { get; init; } = "";
    public string BranchEmail { get; init; } = "";
}
public record LeadImage(Guid Id, string Extension);
