using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Partners;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Domain.Tenants;
using CleaningSuite.Infrastructure.Persistence;
using CleaningSuite.Tests.QualityCycle;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CleaningSuite.Tests.Shifts;

public class StaleAssignmentRepairDatabaseTests
{
    private static Shift ShiftOf(bool active, Guid? partnerRequestId = null)
    {
        var shift = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Shift",
            new ShiftSchedule { Type = ShiftScheduleType.DailySameTime, DailyStart = TimeSpan.FromHours(8), DailyEnd = TimeSpan.FromHours(10) },
            null, DateTime.UtcNow.Date, null, null);
        shift.IsActive = active;
        shift.PartnerRequestId = partnerRequestId;
        return shift;
    }

    [QualityCycleDatabaseFact]
    public async Task Startup_repair_releases_stale_assignments_and_leaves_live_ones_active()
    {
        var connection = Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCleaningMarten(connection, databaseSchemaName: "stale_repair_" + Guid.NewGuid().ToString("N"));
        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var tenant = "stale-" + Guid.NewGuid().ToString("N");
        await new TenantRegistry(store).SaveAsync(new TenantRegistration
        {
            Id = Guid.NewGuid(), Slug = tenant, Status = TenantRegistration.StatusActive,
        });

        var cancelled = new PartnerCleaningRequest { Id = Guid.NewGuid(), Status = PartnerCleaningRequest.StatusCancelled };
        var deactivatedShift = ShiftOf(active: false);
        var cancelledShift = ShiftOf(active: true, partnerRequestId: cancelled.Id);
        var liveShift = ShiftOf(active: true);
        var staleOnDeactivated = ShiftAssignment.Create(deactivatedShift.Id, Guid.NewGuid(), null);
        var staleOnCancelled = ShiftAssignment.Create(cancelledShift.Id, Guid.NewGuid(), null);
        var live = ShiftAssignment.Create(liveShift.Id, Guid.NewGuid(), null);

        using (var seed = store.LightweightSession(tenant))
        {
            seed.Store(cancelled);
            seed.Store(deactivatedShift, cancelledShift, liveShift);
            seed.Store(staleOnDeactivated, staleOnCancelled, live);
            await seed.SaveChangesAsync();
        }

        var runner = new StaleAssignmentRepairRunner(store, new TenantRegistry(store),
            provider.GetRequiredService<ILogger<StaleAssignmentRepairRunner>>());
        var released = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(2, released);
        using var check = store.LightweightSession(tenant);
        var rows = await check.Query<ShiftAssignment>().ToListAsync();
        Assert.False(rows.Single(r => r.Id == staleOnDeactivated.Id).IsActive);
        Assert.False(rows.Single(r => r.Id == staleOnCancelled.Id).IsActive);
        Assert.True(rows.Single(r => r.Id == live.Id).IsActive);

        // A second start finds nothing new to release.
        Assert.Equal(0, await runner.RunAsync(CancellationToken.None));
    }
}
