using CleaningSuite.Application.Entertainment;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CleaningSuite.Api.Services;

/// <summary>
/// Deletes entertainment groups that have not been used for 7 days. Runs once a
/// day at midnight UTC (a weekly check would be equivalent since the 7-day
/// window makes it idempotent).
/// </summary>
public class EntertainmentGroupCleanupService : BackgroundService
{
    private const int IdleDays = 7;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EntertainmentGroupCleanupService> _logger;

    public EntertainmentGroupCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<EntertainmentGroupCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNextMidnightUtc();
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            try
            {
                await CleanupAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Entertainment group cleanup failed.");
            }
        }
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IEntertainmentRepository>();

        var cutoffUtc = DateTime.UtcNow.AddDays(-IdleDays);
        var groups = await repository.ListInactiveGroupsAsync(cutoffUtc, ct);

        foreach (var group in groups)
        {
            await repository.DeleteGroupAsync(group.Id, ct);
            _logger.LogInformation("Deleted inactive entertainment group {GroupId}", group.Id);
        }
    }

    private static TimeSpan TimeUntilNextMidnightUtc()
    {
        var now = DateTime.UtcNow;
        return now.Date.AddDays(1) - now;
    }
}
