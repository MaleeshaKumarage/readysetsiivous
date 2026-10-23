using CleaningSuite.Application.Common;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Leads;
namespace CleaningSuite.Application.Leads;

public class LeadConflictException(string message) : Exception(message);
public record LeadRow(Guid Id, Guid Version, LeadFields Fields, bool IsActive, string Status, int ImageCount, Guid? CompanyId, Guid? BranchId, DateTime? ConvertedUtc, DateTime UpdatedUtc);
public record LeadDetail(LeadRow Lead, IReadOnlyList<LeadImage> Images, bool CustomerAvailable);
public interface ILeadRepository
{
    Task<IAsyncDisposable> LockAsync(Guid id,CancellationToken ct);
    Task<Lead?> GetAsync(Guid id,CancellationToken ct);
    Task<(IReadOnlyList<Lead> Items,int Total)> ListAsync(string search,string status,int skip,int take,CancellationToken ct);
    Task SaveAsync(Lead lead,CancellationToken ct);
    Task DeleteAsync(Lead lead,CancellationToken ct);
    Task ConvertAsync(Lead lead,Company company,Branch branch,CancellationToken ct);
    Task<bool> CustomerAvailableAsync(Guid companyId,Guid branchId,CancellationToken ct);
}
public interface ILeadFileStore
{
    Task SaveAsync(Guid leadId,LeadImage image,byte[] content,CancellationToken ct);
    Task<byte[]?> ReadAsync(Guid leadId,LeadImage image,CancellationToken ct);
    Task DeleteAsync(Guid leadId,LeadImage image);
}
public sealed class LeadService(ILeadRepository repository,ILeadFileStore files,IUploadContentValidator uploads,TimeProvider clock)
{
    public static LeadRow Row(Lead lead) => new(lead.Id,lead.Version,lead.Fields,lead.IsActive,lead.CompanyId.HasValue?"Converted":lead.Fields.Status,
        lead.Images.Count,lead.CompanyId,lead.BranchId,lead.ConvertedUtc,lead.UpdatedUtc);
    private async Task<Lead> Require(Guid id,CancellationToken ct) => await repository.GetAsync(id,ct) ?? throw new NotFoundException("Lead",id);
    public async Task<LeadDetail> GetAsync(Guid id,CancellationToken ct)
    {
        var lead=await Require(id,ct);
        return new(Row(lead),lead.Images,lead.CompanyId.HasValue && lead.BranchId.HasValue && await repository.CustomerAvailableAsync(lead.CompanyId.Value,lead.BranchId.Value,ct));
    }
    public async Task<Paged<LeadRow>> ListAsync(string? search,string? status,int skip,int take,CancellationToken ct)
    {
        if(skip<0 || take is <1 or >100 || (search?.Length??0)>200 || !new[]{"Active","Inactive","Converted","New","Contacted","In discussion","Lost"}.Contains(status??"Active"))
            throw new FluentValidation.ValidationException("Choose a valid lead filter and page size (1–100).");
        var result=await repository.ListAsync(search?.Trim()??"",status??"Active",skip,take,ct);
        return new(result.Items.Select(Row).ToArray(),result.Total);
    }
    public async Task<LeadDetail> CreateAsync(LeadFields fields,CancellationToken ct)
    {
        var lead=new Lead { Fields=LeadValidation.Normalize(fields),CreatedUtc=clock.GetUtcNow().UtcDateTime,UpdatedUtc=clock.GetUtcNow().UtcDateTime };
        await repository.SaveAsync(lead,ct);return new(Row(lead),lead.Images,false);
    }
    private static void Editable(Lead lead,Guid version,bool active=false)
    {
        if(lead.CompanyId.HasValue) throw new LeadConflictException("Converted leads are retained as read-only records. Edit the customer instead.");
        if(lead.Version!=version || version==Guid.Empty) throw new LeadConflictException("This lead changed. Refresh it before retrying; your unsaved changes have not been applied.");
        if(active && !lead.IsActive) throw new LeadConflictException("Reactivate this lead before changing images or converting it.");
    }
    public async Task<LeadDetail> UpdateAsync(Guid id,Guid version,LeadFields fields,CancellationToken ct)
    {
        await using var lease=await repository.LockAsync(id,ct);var lead=await Require(id,ct);Editable(lead,version);
        lead.Fields=LeadValidation.Normalize(fields);lead.UpdatedUtc=clock.GetUtcNow().UtcDateTime;await repository.SaveAsync(lead,ct);
        return new(Row(lead),lead.Images,false);
    }
    public async Task<LeadDetail> SetActiveAsync(Guid id,Guid version,bool active,CancellationToken ct)
    {
        await using var lease=await repository.LockAsync(id,ct);var lead=await Require(id,ct);Editable(lead,version);
        lead.IsActive=active;lead.UpdatedUtc=clock.GetUtcNow().UtcDateTime;await repository.SaveAsync(lead,ct);return new(Row(lead),lead.Images,false);
    }
    public async Task DeleteAsync(Guid id,Guid version,CancellationToken ct)
    {
        await using var lease=await repository.LockAsync(id,ct);var lead=await Require(id,ct);Editable(lead,version);
        if(lead.IsActive) throw new LeadConflictException("Deactivate this lead before deleting it.");
        await repository.DeleteAsync(lead,ct);foreach(var image in lead.Images) await files.DeleteAsync(id,image);
    }
    public async Task<LeadDetail> ConvertAsync(Guid id,Guid version,CancellationToken ct)
    {
        await using var lease=await repository.LockAsync(id,ct);var lead=await Require(id,ct);
        // Idempotent even if the browser retries with the original pre-conversion version.
        if(lead.CompanyId.HasValue) return await GetAsync(id,ct);
        Editable(lead,version,true);var f=LeadValidation.Normalize(lead.Fields);LeadValidation.Conversion(f);
        var company=Company.Create(f.BusinessId,f.Name,f.ContactName,f.PrimaryEmail,f.ContactPhone,
            string.Join("\n\n",new[]{f.Notes,f.Description}.Where(x=>x.Length>0)));
        var branch=Branch.Create(company.Id,f.BranchName,f.Street,f.PostalCode,f.City,f.Country,f.BranchPhone);branch.ContactEmail=f.BranchEmail;
        lead.CompanyId=company.Id;lead.BranchId=branch.Id;lead.ConvertedUtc=clock.GetUtcNow().UtcDateTime;lead.UpdatedUtc=lead.ConvertedUtc.Value;
        await repository.ConvertAsync(lead,company,branch,ct);return new(Row(lead),lead.Images,true);
    }
    public async Task<LeadDetail> UploadAsync(Guid id,Guid version,byte[] content,CancellationToken ct)
    {
        var extension=uploads.ValidateServiceImage(content);
        await using var lease=await repository.LockAsync(id,ct);var lead=await Require(id,ct);Editable(lead,version,true);
        if(lead.Images.Count>=3) throw new LeadConflictException("A lead can have at most 3 images. Remove an image before adding another.");
        var image=new LeadImage(Guid.NewGuid(),extension);await files.SaveAsync(id,image,content,ct);
        try { lead.Images.Add(image);lead.UpdatedUtc=clock.GetUtcNow().UtcDateTime;await repository.SaveAsync(lead,ct); }
        catch { await files.DeleteAsync(id,image);throw; }
        return new(Row(lead),lead.Images,false);
    }
    public async Task<LeadDetail> RemoveImageAsync(Guid id,Guid imageId,Guid version,CancellationToken ct)
    {
        await using var lease=await repository.LockAsync(id,ct);var lead=await Require(id,ct);Editable(lead,version,true);
        var image=lead.Images.SingleOrDefault(x=>x.Id==imageId) ?? throw new NotFoundException("Lead image",imageId);
        lead.Images.Remove(image);lead.UpdatedUtc=clock.GetUtcNow().UtcDateTime;await repository.SaveAsync(lead,ct);await files.DeleteAsync(id,image);
        return new(Row(lead),lead.Images,false);
    }
    public async Task<(byte[] Bytes,string ContentType)> ImageAsync(Guid id,Guid imageId,CancellationToken ct)
    {
        var lead=await Require(id,ct);var image=lead.Images.SingleOrDefault(x=>x.Id==imageId) ?? throw new NotFoundException("Lead image",imageId);
        var bytes=await files.ReadAsync(id,image,ct) ?? throw new NotFoundException("Lead image",imageId);
        return (bytes,image.Extension switch { ".jpg"=>"image/jpeg",".png"=>"image/png",".webp"=>"image/webp",_=>throw new LeadConflictException("Unsupported stored image format.") });
    }
}
