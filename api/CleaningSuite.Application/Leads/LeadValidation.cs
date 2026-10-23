using System.ComponentModel.DataAnnotations;
using CleaningSuite.Domain.Leads;
using FluentValidation;
using FluentValidation.Results;
namespace CleaningSuite.Application.Leads;

public static class LeadValidation
{
    public static readonly string[] Statuses = ["New", "Contacted", "In discussion", "Lost"];
    public static LeadFields Normalize(LeadFields f)
    {
        string Trim(string? value) => value?.Trim() ?? "";
        var result = f with { Name=Trim(f.Name), BusinessId=Trim(f.BusinessId), ContactName=Trim(f.ContactName), ContactPhone=Trim(f.ContactPhone),
            Notes=Trim(f.Notes), Description=Trim(f.Description), BranchName=Trim(f.BranchName), Street=Trim(f.Street), PostalCode=Trim(f.PostalCode),
            City=Trim(f.City), Country=Trim(f.Country), BranchPhone=Trim(f.BranchPhone), BranchEmail=Trim(f.BranchEmail), Status=Trim(f.Status),
            Emails=(f.Emails ?? []).Select(Trim).Where(x=>x.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), PrimaryEmail=Trim(f.PrimaryEmail) };
        var errors = new List<ValidationFailure>();
        void Bound(string key,string value,int max) { if(value.Length>max) errors.Add(new(key,$"{key} must be {max} characters or fewer.")); }
        if(result.Name.Length==0) errors.Add(new("Name","Company name is required."));
        foreach(var (key,value,max) in new[]{("Name",result.Name,200),("BusinessId",result.BusinessId,50),("ContactName",result.ContactName,200),
            ("ContactPhone",result.ContactPhone,50),("Notes",result.Notes,5000),("Description",result.Description,10000),("BranchName",result.BranchName,200),
            ("Street",result.Street,300),("PostalCode",result.PostalCode,30),("City",result.City,100),("Country",result.Country,100),("BranchPhone",result.BranchPhone,50)}) Bound(key,value,max);
        bool ValidEmail(string email) => email.Length<=254 && new EmailAddressAttribute().IsValid(email) && !email.Any(char.IsControl);
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
        if(string.IsNullOrWhiteSpace(fields.BranchName)) errors.Add(new("BranchName","Enter the branch name before converting this lead."));
        if(errors.Count>0) throw new FluentValidation.ValidationException(errors);
    }
}
