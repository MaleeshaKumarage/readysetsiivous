using System.Security.Cryptography;
using System.Text;
using CleaningSuite.Application.Leads;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Leads;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
namespace CleaningSuite.Infrastructure.Leads;

public sealed class DiskLeadFileStore(IConfiguration config,ITenantContext tenant,ILogger<DiskLeadFileStore> logger) : ILeadFileStore
{
    private string PathFor(Guid leadId,LeadImage image)
    {
        if(image.Extension is not (".jpg" or ".png" or ".webp")) throw new LeadConflictException("Unsupported stored image format.");
        var partition=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tenant.TenantId))).ToLowerInvariant();
        return Path.Combine(config["Uploads:Path"]??Path.Combine(Directory.GetCurrentDirectory(),"uploads"),"leads",partition,leadId.ToString("N"),image.Id.ToString("N")+image.Extension);
    }
    public async Task SaveAsync(Guid leadId,LeadImage image,byte[] content,CancellationToken ct)
    {
        var path=PathFor(leadId,image);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var created=false;
        try { await using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);created=true;await stream.WriteAsync(content,ct); }
        catch { if(created) await DeleteAsync(leadId,image);throw; }
    }
    public async Task<byte[]?> ReadAsync(Guid leadId,LeadImage image,CancellationToken ct)
    {
        try { return await File.ReadAllBytesAsync(PathFor(leadId,image),ct); } catch(FileNotFoundException) { return null; } catch(DirectoryNotFoundException) { return null; }
    }
    public Task DeleteAsync(Guid leadId,LeadImage image)
    {
        try { File.Delete(PathFor(leadId,image)); } catch(Exception e) when(e is IOException or UnauthorizedAccessException) { logger.LogError(e,"Unreferenced lead image cleanup failed for {LeadId}/{ImageId}",leadId,image.Id); }
        return Task.CompletedTask;
    }
}
