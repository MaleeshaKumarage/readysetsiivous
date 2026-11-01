using CleaningSuite.Api.Tenancy;
using CleaningSuite.Application.Partners;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Tenants;
namespace CleaningSuite.Api;

public sealed class PartnerNotificationWorker(IServiceScopeFactory scopes, ILogger<PartnerNotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), ct);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
            do
            {
                try
                {
                    using var registry = scopes.CreateScope();
                    var tenants = await registry.ServiceProvider.GetRequiredService<ITenantRegistry>().ListAsync(ct);
                    foreach (var tenant in tenants.Where(t => t.Status == TenantRegistration.StatusActive))
                    {
                        try
                        {
                            using var scope = scopes.CreateScope();
                            scope.ServiceProvider.GetRequiredService<HttpTenantContext>().SetBackgroundTenant(tenant.Slug);
                            await scope.ServiceProvider.GetRequiredService<PartnerNotificationDispatcher>().DispatchAsync(ct);
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                        catch (Exception error) { logger.LogError(error, "Partner notifications failed for tenant {Tenant}", tenant.Slug); }
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception error) { logger.LogError(error, "Partner notification registry scan failed"); }
            } while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
}
