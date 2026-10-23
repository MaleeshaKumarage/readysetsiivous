using CleaningSuite.Application.Leads;
using CleaningSuite.Application.Common;
using CleaningSuite.Domain.Leads;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Tests.Support;
using FluentValidation;
using Moq;
namespace CleaningSuite.Tests.Leads;
public class LeadServiceTests
{
    private static LeadFields Fields()=>new(){Name=" Prospect ",BusinessId="123",BranchName="Office",Emails=["a@example.test","A@example.test","b@example.test"],PrimaryEmail="a@example.test",Description="Description",Notes="Notes"};
    [Fact] public void Contacts_are_normalized_and_secondary_addresses_preserved()
    { var fields=LeadValidation.Normalize(Fields());Assert.Equal("Prospect",fields.Name);Assert.Equal(2,fields.Emails.Count);Assert.Equal("a@example.test",fields.PrimaryEmail); }
    [Theory][InlineData("bad email","bad email")][InlineData("a@example.test","other@example.test")][InlineData("","other@example.test")]
    public void Invalid_email_or_primary_is_rejected(string email,string primary)=>Assert.Throws<ValidationException>(()=>LeadValidation.Normalize(Fields() with{Emails=email.Length>0?[email]:[],PrimaryEmail=primary}));
    [Theory][InlineData("", "Office")]
    public void Conversion_requires_business_id(string business,string branch)=>Assert.Throws<ValidationException>(()=>LeadValidation.Conversion(Fields() with{BusinessId=business,BranchName=branch}));
    [Fact] public async Task Conversion_preserves_every_contact_and_image_and_retry_is_idempotent()
    {
        var f=new Fixture();var originalVersion=f.Lead.Version;f.Lead.Images.Add(new(Guid.NewGuid(),".png"));
        var result=await f.Service.ConvertAsync(f.Lead.Id,originalVersion,default);
        Assert.Equal("Converted",result.Lead.Status);Assert.Equal(2,result.Lead.Fields.Emails.Count);Assert.Single(result.Images);
        Assert.NotNull(f.Company);Assert.Equal("a@example.test",f.Company.ContactEmail);Assert.Contains("Description",f.Company.Notes);Assert.Contains("Notes",f.Company.Notes);
        var retry=await f.Service.ConvertAsync(f.Lead.Id,originalVersion,default);Assert.Equal(result.Lead.CompanyId,retry.Lead.CompanyId);
        f.Repo.Verify(r=>r.ConvertAsync(It.IsAny<Lead>(),It.IsAny<Company>(),It.IsAny<IReadOnlyList<Branch>>(),default),Times.Once);
        await Assert.ThrowsAsync<LeadConflictException>(()=>f.Service.UpdateAsync(f.Lead.Id,f.Lead.Version,Fields(),default));
        await Assert.ThrowsAsync<LeadConflictException>(()=>f.Service.DeleteAsync(f.Lead.Id,f.Lead.Version,default));
    }
    [Fact] public async Task Inactive_fields_are_editable_but_conversion_and_upload_are_blocked()
    {
        var f=new Fixture();f.Lead.IsActive=false;
        await f.Service.UpdateAsync(f.Lead.Id,f.Lead.Version,Fields() with{Name="Edited"},default);
        await Assert.ThrowsAsync<LeadConflictException>(()=>f.Service.ConvertAsync(f.Lead.Id,f.Lead.Version,default));
        await Assert.ThrowsAsync<LeadConflictException>(()=>f.Service.UploadAsync(f.Lead.Id,f.Lead.Version,[1],default));
        await f.Service.SetActiveAsync(f.Lead.Id,f.Lead.Version,true,default);Assert.True(f.Lead.IsActive);
    }
    [Fact] public async Task Fourth_image_and_stale_writes_do_not_create_files()
    {
        var f=new Fixture();f.Lead.Images.AddRange(Enumerable.Range(0,3).Select(_=>new LeadImage(Guid.NewGuid(),".png")));
        await Assert.ThrowsAsync<LeadConflictException>(()=>f.Service.UploadAsync(f.Lead.Id,f.Lead.Version,[1],default));
        await Assert.ThrowsAsync<LeadConflictException>(()=>f.Service.UpdateAsync(f.Lead.Id,Guid.NewGuid(),Fields(),default));
        f.Files.Verify(x=>x.SaveAsync(It.IsAny<Guid>(),It.IsAny<LeadImage>(),It.IsAny<byte[]>(),default),Times.Never);
    }
    [Fact] public async Task Failed_upload_persistence_cleans_only_new_file()
    {
        var f=new Fixture();var existing=new LeadImage(Guid.NewGuid(),".png");f.Lead.Images.Add(existing);
        f.Repo.Setup(r=>r.SaveAsync(It.IsAny<Lead>(),default)).ThrowsAsync(new IOException("fixture"));
        await Assert.ThrowsAsync<IOException>(()=>f.Service.UploadAsync(f.Lead.Id,f.Lead.Version,[1],default));
        f.Files.Verify(x=>x.DeleteAsync(f.Lead.Id,It.Is<LeadImage>(i=>i.Id!=existing.Id)),Times.Once);
        f.Files.Verify(x=>x.DeleteAsync(f.Lead.Id,existing),Times.Never);
    }
    [Fact] public async Task Only_inactive_unconverted_leads_can_be_deleted_and_images_require_membership()
    {
        var f=new Fixture();await Assert.ThrowsAsync<LeadConflictException>(()=>f.Service.DeleteAsync(f.Lead.Id,f.Lead.Version,default));
        await Assert.ThrowsAsync<NotFoundException>(()=>f.Service.ImageAsync(f.Lead.Id,Guid.NewGuid(),default));
        f.Files.Verify(x=>x.ReadAsync(It.IsAny<Guid>(),It.IsAny<LeadImage>(),default),Times.Never);
        f.Lead.IsActive=false;await f.Service.DeleteAsync(f.Lead.Id,f.Lead.Version,default);f.Repo.Verify(r=>r.DeleteAsync(f.Lead,default),Times.Once);
    }
    [Fact] public void Boundaries_and_optional_prospect_fields_are_validated_before_storage()
    {
        Assert.Empty(LeadValidation.Normalize(new(){Name="Prospect"}).Emails);
        foreach(var fields in new[]{Fields() with{Name=new string('x',201)},Fields() with{Description=new string('x',10001)},
            Fields() with{BranchEmail="bad"},Fields() with{Status="Converted"},Fields() with{Emails=Enumerable.Range(0,21).Select(i=>$"e{i}@example.test").ToList()}})
            Assert.Throws<ValidationException>(()=>LeadValidation.Normalize(fields));
    }
    private sealed class Fixture
    {
        public Lead Lead {get;}=new(){Version=Guid.NewGuid(),Fields=LeadValidation.Normalize(Fields())};
        public Mock<ILeadRepository> Repo {get;}=new();public Mock<ILeadFileStore> Files {get;}=new();public LeadService Service {get;}
        public Company? Company {get;private set;}
        public Fixture()
        {
            Repo.Setup(x=>x.LockAsync(Lead.Id,default)).ReturnsAsync(new Lease());Repo.Setup(x=>x.GetAsync(Lead.Id,default)).ReturnsAsync(Lead);
            Repo.Setup(x=>x.SaveAsync(It.IsAny<Lead>(),default)).Callback<Lead,CancellationToken>((l,_)=>l.Version=Guid.NewGuid()).Returns(Task.CompletedTask);
            Repo.Setup(x=>x.ConvertAsync(Lead,It.IsAny<Company>(),It.IsAny<IReadOnlyList<Branch>>(),default)).Callback<Lead,Company,IReadOnlyList<Branch>,CancellationToken>((_,c,_,_)=>Company=c).Returns(Task.CompletedTask);
            Repo.Setup(x=>x.CustomerAvailableAsync(It.IsAny<Guid>(),It.IsAny<IReadOnlyList<Guid>>(),default)).ReturnsAsync(true);
            Service=new(Repo.Object,Files.Object,NoopUploadContentValidator.Instance,TimeProvider.System);
        }
    }
    private sealed class Lease:IAsyncDisposable { public ValueTask DisposeAsync()=>ValueTask.CompletedTask; }
}
