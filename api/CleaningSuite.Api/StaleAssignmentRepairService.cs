using CleaningSuite.Infrastructure.Persistence;
using Microsoft.Extensions.Hosting;

namespace CleaningSuite.Api;

/// <summary>Runs the stale assignment repair once each time the API starts. Failures are logged; the next start retries.</summary>
public sealed class StaleAssignmentRepairService(IServiceScopeFactory scopes, ILogger<StaleAssignmentRepairService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<StaleAssignmentRepairRunner>();
            await runner.RunAsync(stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Stale assignment repair failed; it will be retried on the next start.");
        }
    }
}
