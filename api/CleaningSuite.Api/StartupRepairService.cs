using CleaningSuite.Infrastructure.Persistence;
using Microsoft.Extensions.Hosting;

namespace CleaningSuite.Api;

/// <summary>
/// Runs every data repair in one place, on each API start. Repairs run in order, and each one is isolated: a failure
/// is logged and the others still run. Every repair is safe to repeat, so the next start retries anything that failed.
/// The shift validity correction runs only when DataCorrections:ShiftValidityWindow:Mode is set (Report or Apply).
/// StartupRepairs:Enabled turns the whole service off; it defaults to on.
/// </summary>
public sealed class StartupRepairService(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<StartupRepairService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Test hosts turn repairs off (StartupRepairs:Enabled=false) so they do not touch the tenant registry at start.
        if (!configuration.GetValue("StartupRepairs:Enabled", true)) return;

        await RunAsync("shift validity correction", stoppingToken, async (services, ct) =>
        {
            var mode = configuration["DataCorrections:ShiftValidityWindow:Mode"] ?? "Off";
            if (string.Equals(mode, "Off", StringComparison.OrdinalIgnoreCase)) return;
            await services.GetRequiredService<ShiftValidityCorrectionRunner>().RunAsync(mode, ct);
        });

        await RunAsync("automatic acceptance default", stoppingToken, async (services, ct) =>
            await services.GetRequiredService<AutoAcceptDefaultRunner>().RunAsync(ReleaseCutoff(), ct));

        await RunAsync("stale assignment release", stoppingToken, async (services, ct) =>
            await services.GetRequiredService<StaleAssignmentRepairRunner>().RunAsync(ct));
    }

    // The release time from configuration. Missing or empty means the automatic acceptance switch does nothing.
    private DateTime? ReleaseCutoff()
    {
        var value = configuration["DataCorrections:AutoAcceptDefault:CutoffUtc"];
        if (string.IsNullOrWhiteSpace(value)) return null;
        var parsed = ParseReleaseCutoff(value);
        if (parsed is null)
            logger.LogError("DataCorrections:AutoAcceptDefault:CutoffUtc is not a valid timestamp: {Value}. The automatic acceptance default is skipped until it is fixed.", value);
        return parsed;
    }

    /// <summary>Parses an ISO timestamp (with offset, or taken as UTC) to UTC. Returns null when the text is not a timestamp.</summary>
    public static DateTime? ParseReleaseCutoff(string? value) =>
        !string.IsNullOrWhiteSpace(value) && System.DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.UtcDateTime : null;

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
