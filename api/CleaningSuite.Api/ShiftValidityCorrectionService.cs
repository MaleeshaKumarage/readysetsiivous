using CleaningSuite.Infrastructure.Persistence;
using Microsoft.Extensions.Hosting;

namespace CleaningSuite.Api;

/// <summary>
/// Runs the one-time shift validity correction when the API starts, so each deployment corrects older data once per tenant.
/// Mode comes from DataCorrections:ShiftValidityWindow:Mode. Deployments set Apply through their compose file; when the
/// setting is absent (local runs and test hosts) nothing runs. Failures are logged and retried on the next start.
/// </summary>
public sealed class ShiftValidityCorrectionService(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<ShiftValidityCorrectionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var mode = configuration["DataCorrections:ShiftValidityWindow:Mode"] ?? "Off";
        if (string.Equals(mode, "Off", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            using var scope = scopes.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<ShiftValidityCorrectionRunner>();
            await runner.RunAsync(mode, stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Shift validity correction failed; it will be retried on the next start.");
        }
    }
}
