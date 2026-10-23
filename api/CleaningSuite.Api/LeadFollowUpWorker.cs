using CleaningSuite.Api.Tenancy;
using CleaningSuite.Application.Leads;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Tenants;
namespace CleaningSuite.Api;

public sealed class LeadFollowUpWorker(IServiceScopeFactory scopes,ILogger<LeadFollowUpWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var lastCleanup=DateTime.MinValue;
        try {
            await Task.Delay(TimeSpan.FromSeconds(20),ct);
            using var timer=new PeriodicTimer(TimeSpan.FromMinutes(1));
            do {
                try {
                    using var registry=scopes.CreateScope();
                    var tenants=await registry.ServiceProvider.GetRequiredService<ITenantRegistry>().ListAsync(ct);
                    var cleanup=DateTime.UtcNow-lastCleanup>TimeSpan.FromHours(1);
                    foreach(var tenant in tenants.Where(x=>x.Status==TenantRegistration.StatusActive)) {
                        try {
                            using var scope=scopes.CreateScope();scope.ServiceProvider.GetRequiredService<HttpTenantContext>().SetBackgroundTenant(tenant.Slug);
                            await scope.ServiceProvider.GetRequiredService<LeadFollowUpDispatcher>().DispatchAsync(ct);
                            if(cleanup) await scope.ServiceProvider.GetRequiredService<LeadCollaborationService>().CleanupAsync(ct);
                        } catch(OperationCanceledException) when(ct.IsCancellationRequested) { throw; }
                        catch(Exception e) { logger.LogError(e,"Lead follow-up processing failed for tenant {Tenant}",tenant.Slug); }
                    }
                    if(cleanup) lastCleanup=DateTime.UtcNow;
                } catch(OperationCanceledException) when(ct.IsCancellationRequested) { throw; }
                catch(Exception e) { logger.LogError(e,"Lead follow-up registry scan failed"); }
            } while(await timer.WaitForNextTickAsync(ct));
        } catch(OperationCanceledException) when(ct.IsCancellationRequested) { }
    }
}
