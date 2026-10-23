using CleaningSuite.Application.Leads;
using CleaningSuite.Domain.Leads;
using FluentValidation;
namespace CleaningSuite.Tests.Leads;
public class LeadBranchesTests
{
    [Fact] public void New_lead_and_country_only_legacy_default_have_no_branch()
    {
        var fields=LeadValidation.Normalize(new LeadFields{Name="Company",Country="FI"},Guid.NewGuid());Assert.Empty(fields.Branches!);Assert.Equal("",fields.BranchName);
        var legacy=new Lead{Fields=new(){Name="Company",Country="FI"}};Assert.Empty(LeadBranches.Project(legacy).Branches!);
        LeadValidation.Conversion(fields with { BusinessId="BID" });
    }
    [Fact] public void Legacy_branch_is_read_without_writing_and_uses_a_stable_identity()
    {
        var lead=new Lead{Fields=new(){Name="Company",BranchName="Office",Street="Street",Country="FI"}};
        var first=Assert.Single(LeadBranches.Project(lead).Branches!);Assert.Equal(lead.Id,first.Id);Assert.Equal("Street",first.Street);
        Assert.Null(lead.Fields.Branches);Assert.Equal(first.Id,Assert.Single(LeadBranches.Project(lead).Branches!).Id);
    }
    [Fact] public void Older_clients_edit_first_branch_without_dropping_other_branches_and_explicit_empty_removes_all()
    {
        var first=new LeadBranchFields{Name="First"};var second=new LeadBranchFields{Name="Second"};var existing=new LeadFields{Name="Company",Branches=[first,second]};
        var changed=LeadValidation.Normalize(new LeadFields{Name="Company",BranchName="Updated first"},Guid.NewGuid(),existing);
        Assert.Equal(2,changed.Branches!.Count);Assert.Equal(first.Id,changed.Branches[0].Id);Assert.Equal("Updated first",changed.Branches[0].Name);Assert.Equal(second,changed.Branches[1]);
        Assert.Equal(2,LeadValidation.Normalize(new LeadFields{Name="Company"},Guid.NewGuid(),existing).Branches!.Count);
        var cleared=LeadValidation.Normalize(changed with { Branches=[] },Guid.NewGuid(),existing);Assert.Empty(cleared.Branches!);Assert.Equal("",cleared.BranchName);
        var created=LeadValidation.Normalize(new LeadFields{Name="Company",BranchName="Old client branch"},Guid.NewGuid(),new LeadFields{Name="Company",Branches=[]});Assert.Single(created.Branches!);
    }
    [Fact] public void Every_branch_is_validated_and_ids_cannot_repeat()
    {
        var first=new LeadBranchFields{Name="First"};var fields=new LeadFields{Name="Company",Branches=[first,new(){Name=""}]};
        Assert.Throws<ValidationException>(()=>LeadValidation.Normalize(fields));
        Assert.Throws<ValidationException>(()=>LeadValidation.Normalize(fields with { Branches=[first,first with { Name="Duplicate ID" }] }));
        Assert.Throws<ValidationException>(()=>LeadValidation.Normalize(fields with { Branches=[first,new(){Name="Second",ContactEmail="bad"}] }));
        Assert.Throws<ValidationException>(()=>LeadValidation.Normalize(fields with { Branches=[first,new(){Name="Second",Street=new string('x',301)}] }));
    }
    [Fact] public void Many_branches_are_supported_without_a_small_product_cap()
    {
        var fields=LeadValidation.Normalize(new LeadFields{Name="Company",Branches=Enumerable.Range(0,120).Select(i=>new LeadBranchFields{Name=$"Office {i}"}).ToList()});Assert.Equal(120,fields.Branches!.Count);
    }
}
