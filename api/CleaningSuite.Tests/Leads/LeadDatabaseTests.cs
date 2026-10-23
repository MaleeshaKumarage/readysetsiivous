using CleaningSuite.Application.Leads;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Leads;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Infrastructure.Persistence;
using CleaningSuite.Tests.Support;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
namespace CleaningSuite.Tests.Leads;
public class LeadDatabaseTests
{
    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Tenant_isolation_search_and_atomic_conversion_with_duplicate_rollback()
    {
        using var services=Services();var store=services.GetRequiredService<IDocumentStore>();await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var fields=new LeadFields{Name="Prospect",BusinessId="BID",BranchName="Office",Emails=["one@example.test","two@example.test"],PrimaryEmail="one@example.test",Description="Retained description"};
        LeadDetail original;
        using(var scope=Tenant(store,"a")) original=await Service(scope).CreateAsync(fields,default);
        using(var foreign=Tenant(store,"b")) {
            Assert.Null(await new LeadRepository(foreign,Config()).GetAsync(original.Lead.Id,default));
            await Assert.ThrowsAsync<CleaningSuite.Application.Common.NotFoundException>(()=>Service(foreign).GetAsync(original.Lead.Id,default));
            var result=await Service(foreign).ListAsync("","Active",0,25,default);Assert.Empty(result.Items);
        }
        using(var own=Tenant(store,"a")) {
            var found=await Service(own).ListAsync("TWO@EXAMPLE","Active",0,25,default);Assert.Equal(original.Lead.Id,Assert.Single(found.Items).Id);
            var converted=await Service(own).ConvertAsync(original.Lead.Id,original.Lead.Version,default);
            Assert.True(converted.CustomerAvailable);Assert.Equal(2,converted.Lead.Fields.Emails.Count);
        }
        using(var scope=Tenant(store,"a")) {
            var company=Assert.Single(await scope.Session.Query<Company>().ToListAsync());company.Deactivate();scope.Session.Store(company);await scope.Session.SaveChangesAsync();
        }
        LeadDetail duplicate;
        using(var own=Tenant(store,"a")) duplicate=await Service(own).CreateAsync(fields with{Name="Duplicate"},default);
        using(var own=Tenant(store,"a")) await Assert.ThrowsAsync<LeadConflictException>(()=>Service(own).ConvertAsync(duplicate.Lead.Id,duplicate.Lead.Version,default));
        using(var reader=store.QuerySession("a")) {
            Assert.Single(await reader.Query<Company>().ToListAsync());Assert.Single(await reader.Query<Branch>().ToListAsync());
            Assert.Null((await reader.LoadAsync<Lead>(duplicate.Lead.Id))!.CompanyId);
        }
        using(var foreign=Tenant(store,"b")) {
            var separate=await Service(foreign).CreateAsync(fields,default);await Service(foreign).ConvertAsync(separate.Lead.Id,separate.Lead.Version,default);
        }
    }
    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Concurrent_conversion_is_idempotent_and_stale_edit_cannot_overwrite_it()
    {
        using var services=Services();var store=services.GetRequiredService<IDocumentStore>();await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        LeadDetail original;using(var scope=Tenant(store,"a")) original=await Service(scope).CreateAsync(new(){Name="Concurrent",BusinessId="RACE",BranchName="Office"},default);
        var results=await Task.WhenAll(Enumerable.Range(0,3).Select(async _=>{using var scope=Tenant(store,"a");return await Service(scope).ConvertAsync(original.Lead.Id,original.Lead.Version,default);}));
        Assert.Single(results.Select(x=>x.Lead.CompanyId).Distinct());
        using(var scope=Tenant(store,"a")) await Assert.ThrowsAsync<LeadConflictException>(()=>Service(scope).UpdateAsync(original.Lead.Id,original.Lead.Version,original.Lead.Fields,default));
        using var reader=store.QuerySession("a");Assert.Single(await reader.Query<Company>().ToListAsync());Assert.Single(await reader.Query<Branch>().ToListAsync());
    }
    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Concurrent_uploads_cannot_exceed_three_and_stale_version_is_rejected()
    {
        using var services=Services();var store=services.GetRequiredService<IDocumentStore>();await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        LeadDetail lead;using(var scope=Tenant(store,"a")) {
            lead=await Service(scope).CreateAsync(new(){Name="Images"},default);
            lead=await Service(scope).UploadAsync(lead.Lead.Id,lead.Lead.Version,[1],default);
            lead=await Service(scope).UploadAsync(lead.Lead.Id,lead.Lead.Version,[1],default);
        }
        var outcomes=await Task.WhenAll(Enumerable.Range(0,2).Select(async _=>{using var scope=Tenant(store,"a");try{await Service(scope).UploadAsync(lead.Lead.Id,lead.Lead.Version,[1],default);return true;}catch(LeadConflictException){return false;}}));
        Assert.Single(outcomes.Where(x=>x));using var reader=store.QuerySession("a");Assert.Equal(3,(await reader.LoadAsync<Lead>(lead.Lead.Id))!.Images.Count);
    }
    private static IConfiguration Config()=>new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["ConnectionStrings:DefaultConnection"]=Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")}).Build();
    private static ServiceProvider Services(){var s=new ServiceCollection();s.AddLogging();s.AddCleaningMarten(Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!,databaseSchemaName:"leads_"+Guid.NewGuid().ToString("N"));return s.BuildServiceProvider();}
    private static TenantSessionFactory Tenant(IDocumentStore store,string id){var tenant=new Mock<ITenantContext>();tenant.SetupGet(x=>x.TenantId).Returns(id);return new(store,tenant.Object);}
    private static LeadService Service(ITenantSession session)=>new(new LeadRepository(session,Config()),new Mock<ILeadFileStore>().Object,NoopUploadContentValidator.Instance,TimeProvider.System);
}
