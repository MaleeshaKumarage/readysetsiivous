using CleaningSuite.Api.Tenancy;
using CleaningSuite.Application.Notifications;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Tenants;

namespace CleaningSuite.Api;

public sealed class PushNotificationWorker(IServiceScopeFactory scopes, ILogger<PushNotificationWorker> logger) : BackgroundService
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
                    foreach (var tenant in tenants.Where(x => x.Status == TenantRegistration.StatusActive))
                    {
                        try
                        {
                            using var scope = scopes.CreateScope();
                            scope.ServiceProvider.GetRequiredService<HttpTenantContext>().SetBackgroundTenant(tenant.Slug);
                            if (!scope.ServiceProvider.GetRequiredService<IPushSettings>().Available) continue;
                            await scope.ServiceProvider.GetRequiredService<PushReminderScheduler>().ScheduleAsync(ct);
                            await scope.ServiceProvider.GetRequiredService<IPushDeliveryService>().ProcessAsync(ct);
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                        catch { logger.LogError("Push notification cycle failed for tenant {Tenant}; delivery credentials are omitted", tenant.Slug); }
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch { logger.LogError("Push notification tenant scan failed"); }
            } while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
}
