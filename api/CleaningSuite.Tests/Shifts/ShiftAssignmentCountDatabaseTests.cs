using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Infrastructure.Persistence;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CleaningSuite.Tests.Shifts;

/// <summary>Bulk Employees counts for the shift list, read on PostgreSQL.</summary>
public class ShiftAssignmentCountDatabaseTests
{
    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Counts_only_active_assignments_per_shift_within_the_current_tenant()
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddCleaningMarten(Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!,
            databaseSchemaName: "assignment_counts_" + Guid.NewGuid().ToString("N"));
        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var tenant = "counts-" + Guid.NewGuid().ToString("N"); var other = "counts-other-" + Guid.NewGuid().ToString("N");
        Guid busy = Guid.NewGuid(), quiet = Guid.NewGuid(), foreign = Guid.NewGuid();
        ShiftAssignment Assignment(Guid shift, bool active) => new() { ShiftId = shift, EmployeeId = Guid.NewGuid(), IsActive = active, AssignedAtUtc = DateTime.UtcNow };
        using (var db = store.LightweightSession(tenant))
        {
            db.Store(Assignment(busy, true), Assignment(busy, true), Assignment(busy, false), Assignment(quiet, false));
            await db.SaveChangesAsync();
        }
        using (var db = store.LightweightSession(other))
        {
            db.Store(Assignment(foreign, true), Assignment(busy, true));
            await db.SaveChangesAsync();
        }

        var context = new Mock<ITenantContext>(); context.SetupGet(x => x.TenantId).Returns(tenant);
        using var session = new TenantSessionFactory(store, context.Object);
        var counts = await new ShiftRepository(session).CountActiveAssignmentsByShiftAsync();

        // Inactive-only shifts are absent (the UI shows 0), and the other tenant's rows are never counted.
        var only = Assert.Single(counts);
        Assert.Equal(busy, only.Key);
        Assert.Equal(2, only.Value);
    }
}
