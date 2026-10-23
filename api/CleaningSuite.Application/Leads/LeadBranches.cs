using CleaningSuite.Domain.Leads;
namespace CleaningSuite.Application.Leads;
public static class LeadBranches
{
    public static bool HasLegacy(LeadFields fields)=>new[]{fields.BranchName,fields.Street,fields.PostalCode,fields.City,fields.BranchPhone,fields.BranchEmail}.Any(x=>!string.IsNullOrWhiteSpace(x));
    public static LeadBranchFields Legacy(LeadFields fields,Guid id)=>new(){Id=id,Name=fields.BranchName,Street=fields.Street,PostalCode=fields.PostalCode,City=fields.City,Country=fields.Country,ContactPhone=fields.BranchPhone,ContactEmail=fields.BranchEmail};
    public static IReadOnlyList<LeadBranchFields> Resolve(LeadFields fields,Guid legacyId)=>fields.Branches??(HasLegacy(fields)?[Legacy(fields,legacyId)]:[]);
    public static LeadFields Project(Lead lead)=>lead.Fields with { Branches=Resolve(lead.Fields,lead.Id).ToList() };
    public static IReadOnlyList<Guid> ConvertedIds(Lead lead)=>lead.ConvertedBranches?.Select(x=>x.CompanyBranchId).ToArray()??(lead.BranchId.HasValue?[lead.BranchId.Value]:[]);
}
