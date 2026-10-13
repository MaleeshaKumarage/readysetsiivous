using CleaningSuite.Api.Tenancy;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Tenants;

namespace CleaningSuite.Api;

public sealed class QualityCycleWorker(IServiceScopeFactory scopes, ILogger<QualityCycleWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
            do
            {
                try
                {
                    using var registryScope = scopes.CreateScope();
                    var tenants = await registryScope.ServiceProvider.GetRequiredService<ITenantRegistry>().ListAsync(stoppingToken);
                    foreach (var tenant in tenants.Where(t => t.Status == TenantRegistration.StatusActive))
                    {
                        try
                        {
                            using var scope = scopes.CreateScope();
                            scope.ServiceProvider.GetRequiredService<HttpTenantContext>().SetBackgroundTenant(tenant.Slug);
                            await scope.ServiceProvider.GetRequiredService<IQualityCycleDispatcher>().EnsureUpcomingAsync(ct: stoppingToken);
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                        catch (Exception ex) { logger.LogError(ex, "QC generation failed for tenant {Tenant}", tenant.Slug); }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception ex) { logger.LogError(ex, "QC generation registry scan failed"); }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
