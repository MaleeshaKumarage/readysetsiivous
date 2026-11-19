using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Partners;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Domain.Tenants;
using CleaningSuite.Infrastructure.Persistence;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CleaningSuite.Tests.Shifts;

[Collection("Database integration")]
public class StaleAssignmentRepairDatabaseTests
{
    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Repair_backs_up_then_releases_stale_assignments_and_leaves_live_ones_alone()
    {
        var tenant = "stale-repair-" + Guid.NewGuid().ToString("N");
        var services = new ServiceCollection(); services.AddLogging();
        services.AddTestMarten(Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!, databaseSchemaName: "stale_repair_" + Guid.NewGuid().ToString("N"));
        using var provider = services.BuildServiceProvider(); var store = provider.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var employee = Guid.NewGuid();
        var deactivated = new Shift { Id = Guid.NewGuid(), Name = "Old shift", IsActive = false };
        var live = new Shift { Id = Guid.NewGuid(), Name = "Live shift", IsActive = true };
        var stale = ShiftAssignment.Create(deactivated.Id, employee, "Cover");
        var keep = ShiftAssignment.Create(live.Id, employee, null);
        using (var writer = store.LightweightSession(tenant))
        {
            writer.Store(deactivated); writer.Store(live); writer.Store(stale); writer.Store(keep);
            await writer.SaveChangesAsync();
        }

        var registry = new Mock<ITenantRegistry>();
        registry.Setup(x => x.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([new TenantRegistration { Slug = tenant, Status = TenantRegistration.StatusActive }]);
        var released = await new StaleAssignmentRepairRunner(store, registry.Object, NullLogger<StaleAssignmentRepairRunner>.Instance).RunAsync(default);
        Assert.Equal(1, released);

        using var read = store.QuerySession(tenant);
        var after = await read.LoadAsync<ShiftAssignment>(stale.Id);
        Assert.False(after!.IsActive);
        Assert.True((await read.LoadAsync<ShiftAssignment>(keep.Id))!.IsActive);

        // The backup holds the state before release: the assignment was still active.
        var backup = Assert.Single(await read.Query<StaleAssignmentBackup>().ToListAsync());
        var snapshot = Assert.Single(backup.Assignments);
        Assert.Equal(stale.Id, snapshot.AssignmentId);
        Assert.True(snapshot.IsActive);
        Assert.Equal("Cover", snapshot.Note);

        // A later run finds nothing new, so it writes no second backup.
        Assert.Equal(0, await new StaleAssignmentRepairRunner(store, registry.Object, NullLogger<StaleAssignmentRepairRunner>.Instance).RunAsync(default));
        Assert.Single(await read.Query<StaleAssignmentBackup>().ToListAsync());
    }
}
