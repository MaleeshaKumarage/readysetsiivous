using CleaningSuite.Application.Leads;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Leads;
using CleaningSuite.Infrastructure.Leads;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
namespace CleaningSuite.Tests.Leads;
public class LeadAudioStoreTests
{
    [Fact] public async Task Private_storage_is_tenant_scoped_and_recovers_identical_orphans_without_overwriting_other_content()
    {
        var root=Path.Combine(Path.GetTempPath(),"lead-audio-test-"+Guid.NewGuid().ToString("N"));
        try {
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Uploads:Path",root}}).Build();
            DiskLeadAudioStore Store(string tenant) { var context=new Mock<ITenantContext>();context.SetupGet(x=>x.TenantId).Returns(tenant);return new(config,context.Object,NullLogger<DiskLeadAudioStore>.Instance); }
            var a=Store("tenant-a");var b=Store("tenant-b");var note=new LeadVoiceNote{LeadId=Guid.NewGuid(),Extension=".webm"};
            await a.SaveAsync(note,[1,2],default);Assert.Equal(new byte[]{1,2},await a.ReadAsync(note,default));Assert.Null(await b.ReadAsync(note,default));
            await a.SaveAsync(note,[1,2],default);await Assert.ThrowsAsync<LeadConflictException>(()=>a.SaveAsync(note,[3],default));Assert.Equal(new byte[]{1,2},await a.ReadAsync(note,default));
            await Assert.ThrowsAsync<LeadConflictException>(()=>a.ReadAsync(new LeadVoiceNote{LeadId=note.LeadId,Extension="../../bad"},default));
            await a.DeleteAsync(note);await a.DeleteAsync(note);Assert.Null(await a.ReadAsync(note,default));
        } finally { if(Directory.Exists(root) && Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)) Directory.Delete(root,true); }
    }
    [Fact] public async Task Bounded_cleanup_sweep_advances_past_referenced_files()
    {
        var root=Path.Combine(Path.GetTempPath(),"lead-audio-sweep-"+Guid.NewGuid().ToString("N"));
        try {
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Uploads:Path",root}}).Build();var context=new Mock<ITenantContext>();context.SetupGet(x=>x.TenantId).Returns("tenant-a");
            var store=new DiskLeadAudioStore(config,context.Object,NullLogger<DiskLeadAudioStore>.Instance);var leadId=Guid.NewGuid();
            for(var i=0;i<101;i++) await store.SaveAsync(new LeadVoiceNote{LeadId=leadId,Extension=".webm"},[1],default);
            foreach(var path in Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories)) File.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddDays(-2));
            var first=await store.UnreferencedCandidatesAsync(DateTime.UtcNow.AddDays(-1),default);var second=await store.UnreferencedCandidatesAsync(DateTime.UtcNow.AddDays(-1),default);
            Assert.Equal(100,first.Count);Assert.Single(second);Assert.Equal(101,first.Concat(second).Select(x=>x.NoteId).Distinct().Count());
        } finally { if(Directory.Exists(root) && Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)) Directory.Delete(root,true); }
    }
}
