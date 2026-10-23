using CleaningSuite.Application.Leads;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Leads;
using CleaningSuite.Infrastructure.Leads;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
namespace CleaningSuite.Tests.Leads;
public class LeadPrivateFileStoreTests
{
    [Fact] public async Task Files_are_tenant_isolated_and_generated_paths_cannot_escape_storage()
    {
        var root=Path.Combine(Path.GetTempPath(),"leads-files-"+Guid.NewGuid().ToString("N"));
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Uploads:Path"]=root}).Build();
        DiskLeadFileStore Store(string id){var t=new Mock<ITenantContext>();t.SetupGet(x=>x.TenantId).Returns(id);return new(config,t.Object,NullLogger<DiskLeadFileStore>.Instance);}
        var leadId=Guid.NewGuid();var image=new LeadImage(Guid.NewGuid(),".png");var own=Store("../../tenant-a");var foreign=Store("b");
        try {
            await own.SaveAsync(leadId,image,[1,2,3],default);
            Assert.Null(await foreign.ReadAsync(leadId,image,default));Assert.Equal(new byte[]{1,2,3},await own.ReadAsync(leadId,image,default));
            await Assert.ThrowsAsync<LeadConflictException>(()=>own.SaveAsync(leadId,new(Guid.NewGuid(),"../../outside"),[1],default));
            await own.DeleteAsync(leadId,image);await own.DeleteAsync(leadId,image);Assert.Null(await own.ReadAsync(leadId,image,default));
        } finally {
            var resolved=Path.GetFullPath(root);Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),resolved,StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith("leads-files-",Path.GetFileName(resolved));if(Directory.Exists(resolved)) Directory.Delete(resolved,true);
        }
    }
}
