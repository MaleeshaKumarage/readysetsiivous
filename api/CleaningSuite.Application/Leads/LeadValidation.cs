using System.ComponentModel.DataAnnotations;
using CleaningSuite.Domain.Leads;
using FluentValidation;
using FluentValidation.Results;
namespace CleaningSuite.Application.Leads;

public static class LeadValidation
{
    public static readonly string[] Statuses = ["New", "Contacted", "In discussion", "Lost"];
    public static LeadFields Normalize(LeadFields f,Guid? leadId=null,LeadFields? existing=null)
    {
        string Trim(string? value) => value?.Trim() ?? "";
        var result = f with { Name=Trim(f.Name), BusinessId=Trim(f.BusinessId), ContactName=Trim(f.ContactName), ContactPhone=Trim(f.ContactPhone),
            Notes=Trim(f.Notes), Description=Trim(f.Description), BranchName=Trim(f.BranchName), Street=Trim(f.Street), PostalCode=Trim(f.PostalCode),
            City=Trim(f.City), Country=Trim(f.Country), BranchPhone=Trim(f.BranchPhone), BranchEmail=Trim(f.BranchEmail), Status=Trim(f.Status),
            Emails=(f.Emails ?? []).Select(Trim).Where(x=>x.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), PrimaryEmail=Trim(f.PrimaryEmail) };
        var branches=f.Branches;
        if(branches is null) {
            var prior=existing is null?new List<LeadBranchFields>():LeadBranches.Resolve(existing,leadId??Guid.NewGuid()).ToList();
            if(LeadBranches.HasLegacy(f)) {
                var first=LeadBranches.Legacy(f,prior.FirstOrDefault()?.Id??leadId??Guid.NewGuid());
                if(prior.Count>0) prior[0]=first;else prior.Add(first);
            }
            branches=prior;
        }
        var normalized=branches.Select(b=>b is null?throw new FluentValidation.ValidationException("A branch must have valid fields."):b with {
            Name=Trim(b.Name),Street=Trim(b.Street),PostalCode=Trim(b.PostalCode),City=Trim(b.City),Country=Trim(b.Country),ContactPhone=Trim(b.ContactPhone),ContactEmail=Trim(b.ContactEmail) }).ToList();
        var firstBranch=normalized.FirstOrDefault()??new LeadBranchFields();
        result=result with { Branches=normalized,BranchName=firstBranch.Name,Street=firstBranch.Street,PostalCode=firstBranch.PostalCode,City=firstBranch.City,Country=firstBranch.Country,BranchPhone=firstBranch.ContactPhone,BranchEmail=firstBranch.ContactEmail };
        var errors = new List<ValidationFailure>();
        void Bound(string key,string value,int max) { if(value.Length>max) errors.Add(new(key,$"{key} must be {max} characters or fewer.")); }
        if(result.Name.Length==0) errors.Add(new("Name","Company name is required."));
        foreach(var (key,value,max) in new[]{("Name",result.Name,200),("BusinessId",result.BusinessId,50),("ContactName",result.ContactName,200),
            ("ContactPhone",result.ContactPhone,50),("Notes",result.Notes,5000),("Description",result.Description,10000),("BranchName",result.BranchName,200),
            ("Street",result.Street,300),("PostalCode",result.PostalCode,30),("City",result.City,100),("Country",result.Country,100),("BranchPhone",result.BranchPhone,50)}) Bound(key,value,max);
        bool ValidEmail(string email) => email.Length<=254 && new EmailAddressAttribute().IsValid(email) && !email.Any(char.IsControl);
        if(normalized.Any(b=>b.Id==Guid.Empty) || normalized.Select(b=>b.Id).Distinct().Count()!=normalized.Count) errors.Add(new("Branches","Every branch needs a unique ID."));
        foreach(var b in normalized) {
            if(b.Name.Length==0) errors.Add(new("Branches","Enter a name for each added branch, or remove the draft branch."));
            foreach(var (key,value,max) in new[]{("Branch name",b.Name,200),("Street",b.Street,300),("Postal code",b.PostalCode,30),("City",b.City,100),("Country",b.Country,100),("Branch phone",b.ContactPhone,50)}) Bound(key,value,max);
            if(b.ContactEmail.Length>0 && !ValidEmail(b.ContactEmail)) errors.Add(new("Branches","Enter a valid email for each branch."));
        }
        if(result.Emails.Count>20 || (f.Emails?.Count ?? 0)>100) errors.Add(new("Emails","Use at most 20 distinct email addresses."));
        if(result.Emails.Any(x=>!ValidEmail(x))) errors.Add(new("Emails","Enter valid email addresses (maximum 254 characters each)."));
        if(result.BranchEmail.Length>0 && !ValidEmail(result.BranchEmail)) errors.Add(new("BranchEmail","Enter a valid branch email address."));
        if(result.Emails.Count==0 ? result.PrimaryEmail.Length>0 : !result.Emails.Contains(result.PrimaryEmail,StringComparer.OrdinalIgnoreCase))
            errors.Add(new("PrimaryEmail","Choose a primary email from the contact email list."));
        if(!Statuses.Contains(result.Status)) errors.Add(new("Status","Choose a supported lead status."));
        if(errors.Count>0) throw new FluentValidation.ValidationException(errors);
        return result with { PrimaryEmail=result.Emails.FirstOrDefault(x=>x.Equals(result.PrimaryEmail,StringComparison.OrdinalIgnoreCase)) ?? "" };
    }
    public static void Conversion(LeadFields fields)
    {
        var errors=new List<ValidationFailure>();
        if(string.IsNullOrWhiteSpace(fields.BusinessId)) errors.Add(new("BusinessId","Enter the business ID before converting this lead."));

        if(errors.Count>0) throw new FluentValidation.ValidationException(errors);
    }
}
