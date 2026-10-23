using CleaningSuite.Application.Leads;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Leads;
using CleaningSuite.Infrastructure.Persistence;
using CleaningSuite.Tests.Support;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
namespace CleaningSuite.Tests.Leads;
public class LeadCollaborationDatabaseTests
{
    private static IConfiguration Config()=>new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
        ["ConnectionStrings:DefaultConnection"]=Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING"),["App:FrontendBaseUrl"]="https://app.test"}).Build();
    private static ServiceProvider Services() { var services=new ServiceCollection();services.AddLogging();services.AddCleaningMarten(Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!,databaseSchemaName:"lead_collab_"+Guid.NewGuid().ToString("N"));return services.BuildServiceProvider(); }
    private static TenantSessionFactory Tenant(IDocumentStore store,string slug="tenant-a") { var context=new Mock<ITenantContext>();context.SetupGet(x=>x.TenantId).Returns(slug);return new(store,context.Object); }
    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Conversion_cancels_pending_work_atomically_retains_audio_and_inactive_delete_removes_history()
    {
        using var services=Services();var store=services.GetRequiredService<IDocumentStore>();await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var lead=new Lead{Fields=new(){Name="Prospect",BusinessId="COLLAB",BranchName="Office"}};
        var follow=new LeadFollowUp{LeadId=lead.Id};var note=new LeadVoiceNote{LeadId=lead.Id,Extension=".webm"};var delivery=new LeadFollowUpDelivery{LeadId=lead.Id,FollowUpId=follow.Id};
        using(var session=store.LightweightSession("tenant-a")) { session.Store(lead);session.Store(follow);session.Store(note);session.Store(delivery);await session.SaveChangesAsync(); }
        using(var tenant=Tenant(store)) { var service=new LeadService(new LeadRepository(tenant,Config()),Mock.Of<ILeadFileStore>(),NoopUploadContentValidator.Instance,TimeProvider.System);await service.ConvertAsync(lead.Id,lead.Version,default); }
        using(var session=store.QuerySession("tenant-a")) {
            Assert.Equal("Cancelled",(await session.LoadAsync<LeadFollowUp>(follow.Id))!.State);Assert.Equal("Cancelled",(await session.LoadAsync<LeadFollowUpDelivery>(delivery.Id))!.State);
            Assert.NotNull(await session.LoadAsync<LeadVoiceNote>(note.Id));Assert.NotNull((await session.LoadAsync<Lead>(lead.Id))!.CompanyId);
        }
        using(var foreign=Tenant(store,"other")) { var repo=new LeadCollaborationRepository(foreign);Assert.Empty((await repo.VoicesAsync(lead.Id,0,25,default)).Items);Assert.Null(await repo.FollowUpAsync(lead.Id,follow.Id,default)); }
        var inactive=new Lead{IsActive=false,Fields=new(){Name="Inactive"}};var oldFollow=new LeadFollowUp{LeadId=inactive.Id,State="Cancelled"};var oldNote=new LeadVoiceNote{LeadId=inactive.Id,Extension=".webm"};var history=new LeadFollowUpDelivery{LeadId=inactive.Id,FollowUpId=oldFollow.Id,State="Sent"};
        using(var session=store.LightweightSession("tenant-a")) { session.Store(inactive);session.Store(oldFollow);session.Store(oldNote);session.Store(history);await session.SaveChangesAsync(); }
        using(var tenant=Tenant(store)) { var service=new LeadService(new LeadRepository(tenant,Config()),Mock.Of<ILeadFileStore>(),NoopUploadContentValidator.Instance,TimeProvider.System);await service.DeleteAsync(inactive.Id,inactive.Version,default); }
        using(var session=store.QuerySession("tenant-a")) { Assert.Null(await session.LoadAsync<LeadVoiceNote>(oldNote.Id));Assert.Null(await session.LoadAsync<LeadFollowUp>(oldFollow.Id));Assert.Null(await session.LoadAsync<LeadFollowUpDelivery>(history.Id)); }
    }
    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Zero_and_multiple_branch_conversion_are_atomic_and_retry_keeps_all_mappings()
    {
        using var services=Services();var store=services.GetRequiredService<IDocumentStore>();await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        foreach(var count in new[]{0,3}) {
            LeadDetail created;
            using(var tenant=Tenant(store)) {
                var service=new LeadService(new LeadRepository(tenant,Config()),Mock.Of<ILeadFileStore>(),NoopUploadContentValidator.Instance,TimeProvider.System);
                created=await service.CreateAsync(new(){Name=$"Company {count}",BusinessId=$"MULTI-{count}",Branches=Enumerable.Range(1,count).Select(i=>new LeadBranchFields{Name=$"Branch {i}"}).ToList()},default);
            }
            using(var tenant=Tenant(store)) {
                var service=new LeadService(new LeadRepository(tenant,Config()),Mock.Of<ILeadFileStore>(),NoopUploadContentValidator.Instance,TimeProvider.System);
                var converted=await service.ConvertAsync(created.Lead.Id,created.Lead.Version,default);Assert.Equal(count,converted.Lead.ConvertedBranches!.Count);Assert.True(converted.CustomerAvailable);
                var retry=await service.ConvertAsync(created.Lead.Id,created.Lead.Version,default);Assert.Equal(converted.Lead.CompanyId,retry.Lead.CompanyId);Assert.Equal(converted.Lead.ConvertedBranches,retry.Lead.ConvertedBranches);
                using var reader=store.QuerySession("tenant-a");Assert.Equal(count,await reader.Query<CleaningSuite.Domain.Companies.Branch>().Where(x=>x.CompanyId==converted.Lead.CompanyId!.Value).CountAsync());
            }
        }
        LeadDetail duplicate;
        using(var tenant=Tenant(store)) duplicate=await new LeadService(new LeadRepository(tenant,Config()),Mock.Of<ILeadFileStore>(),NoopUploadContentValidator.Instance,TimeProvider.System).CreateAsync(new(){Name="Duplicate",BusinessId="MULTI-3",Branches=[new(){Name="Extra A"},new(){Name="Extra B"}]},default);
        using(var tenant=Tenant(store)) await Assert.ThrowsAsync<LeadConflictException>(()=>new LeadService(new LeadRepository(tenant,Config()),Mock.Of<ILeadFileStore>(),NoopUploadContentValidator.Instance,TimeProvider.System).ConvertAsync(duplicate.Lead.Id,duplicate.Lead.Version,default));
        using(var reader=store.QuerySession("tenant-a")) { Assert.Equal(2,await reader.Query<CleaningSuite.Domain.Companies.Company>().CountAsync());Assert.Equal(3,await reader.Query<CleaningSuite.Domain.Companies.Branch>().CountAsync());Assert.Null((await reader.LoadAsync<Lead>(duplicate.Lead.Id))!.CompanyId); }
    }
    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Two_workers_send_one_delivery_and_persist_canonical_history()
    {
        using var services=Services();var store=services.GetRequiredService<IDocumentStore>();await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var f=new LeadCollaborationTests.Fixture();var follow=f.AddFollow("Once");
        using(var session=store.LightweightSession("tenant-a")) { session.Store(f.Lead);session.Store(follow);await session.SaveChangesAsync(); }
        async Task Worker() {
            using var tenant=Tenant(store);var dispatcher=new LeadFollowUpDispatcher(new LeadRepository(tenant,Config()),new LeadCollaborationRepository(tenant),f.Directory.Object,f.Transport.Object,
                f.Profiles.Object,f.Tenant.Object,f.Clock,Config(),f.Registry.Object,NullLogger<LeadFollowUpDispatcher>.Instance);
            await dispatcher.DispatchAsync(default);
        }
        await Task.WhenAll(Worker(),Worker());
        f.Transport.Verify(x=>x.SendAsync(It.IsAny<LeadFollowUpDelivery>(),It.IsAny<CancellationToken>()),Times.Once);
        using(var session=store.QuerySession("tenant-a")) { var actual=Assert.Single(await session.Query<LeadFollowUpDelivery>().ToListAsync());Assert.Equal("Sent",actual.State);Assert.Equal("Completed",(await session.LoadAsync<LeadFollowUp>(follow.Id))!.State); }
        using(var tenant=Tenant(store)) { var repo=new LeadCollaborationRepository(tenant);Assert.Equal(1,(await repo.HistoryAsync(f.Lead.Id,0,25,default)).Total);Assert.Empty(await repo.DueAsync(f.Clock.Utc.UtcDateTime,default)); }
    }
}
