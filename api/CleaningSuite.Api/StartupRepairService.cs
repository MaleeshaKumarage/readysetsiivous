using CleaningSuite.Infrastructure.Persistence;
using Microsoft.Extensions.Hosting;

namespace CleaningSuite.Api;

/// <summary>
/// Runs every data repair in one place, on each API start. Repairs run in order, and each one is isolated: a failure
/// is logged and the others still run. Every repair is safe to repeat, so the next start retries anything that failed.
/// The shift validity correction runs only when DataCorrections:ShiftValidityWindow:Mode is set (Report or Apply).
/// </summary>
public sealed class StartupRepairService(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<StartupRepairService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunAsync("shift validity correction", stoppingToken, async (services, ct) =>
        {
            var mode = configuration["DataCorrections:ShiftValidityWindow:Mode"] ?? "Off";
            if (string.Equals(mode, "Off", StringComparison.OrdinalIgnoreCase)) return;
            await services.GetRequiredService<ShiftValidityCorrectionRunner>().RunAsync(mode, ct);
        });

        await RunAsync("stale assignment release", stoppingToken, async (services, ct) =>
            await services.GetRequiredService<StaleAssignmentRepairRunner>().RunAsync(ct));
    }

    private async Task RunAsync(string repair, CancellationToken ct, Func<IServiceProvider, CancellationToken, Task> run)
    {
        try
        {
            using var scope = scopes.CreateScope();
            await run(scope.ServiceProvider, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Startup repair {Repair} failed; it will be retried on the next start.", repair);
        }
    }
}
