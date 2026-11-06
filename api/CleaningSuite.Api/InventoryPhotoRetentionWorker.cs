using CleaningSuite.Api.Tenancy;
using CleaningSuite.Application.Inventory;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Tenants;

namespace CleaningSuite.Api;

/// <summary>Deletes inventory photos older than three months, once an hour, for every active tenant.</summary>
public sealed class InventoryPhotoRetentionWorker(IServiceScopeFactory scopes, ILogger<InventoryPhotoRetentionWorker> logger) : BackgroundService
{
    private const int BatchSize = 200;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(45), ct);
            using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
            do
            {
                try
                {
                    using var registry = scopes.CreateScope();
                    var tenants = await registry.ServiceProvider.GetRequiredService<ITenantRegistry>().ListAsync(ct);
                    foreach (var tenant in tenants.Where(x => x.Status == TenantRegistration.StatusActive))
                    {
                        try
                        {
                            using var scope = scopes.CreateScope();
                            scope.ServiceProvider.GetRequiredService<HttpTenantContext>().SetBackgroundTenant(tenant.Slug);
                            var service = scope.ServiceProvider.GetRequiredService<InventoryService>();
                            int purged;
                            do { purged = await service.PurgeExpiredPhotosAsync(DateTime.UtcNow, BatchSize, ct); }
                            while (purged == BatchSize);
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                        catch (Exception e) { logger.LogError(e, "Inventory photo retention failed for tenant {Tenant}", tenant.Slug); }
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception e) { logger.LogError(e, "Inventory photo retention registry scan failed"); }
            } while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
}
