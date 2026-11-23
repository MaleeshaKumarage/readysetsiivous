using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Partners;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Infrastructure.Persistence;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CleaningSuite.Tests.Shifts;

[Collection("Database integration")]
public class StaleAssignmentDatabaseTests
{
    private static Mock<ITenantContext> Context(string id) { var mock = new Mock<ITenantContext>(); mock.SetupGet(x => x.TenantId).Returns(id); return mock; }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Dry_run_lists_stale_assignments_from_real_storage_and_writes_nothing()
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddTestMarten(Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!, databaseSchemaName: "stale_dry_" + Guid.NewGuid().ToString("N"));
        using var provider = services.BuildServiceProvider(); var store = provider.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var employee = Guid.NewGuid(); var cancelledRequest = Guid.NewGuid(); var liveRequest = Guid.NewGuid();
        var deactivated = new Shift { Id = Guid.NewGuid(), Name = "Old shift", IsActive = false };
        var cancelled = new Shift { Id = Guid.NewGuid(), Name = "Cancelled partner shift", IsActive = true, PartnerRequestId = cancelledRequest };
        var live = new Shift { Id = Guid.NewGuid(), Name = "Live shift", IsActive = true, PartnerRequestId = liveRequest };
        var staleOnDeactivated = ShiftAssignment.Create(deactivated.Id, employee, null);
        var staleOnCancelled = ShiftAssignment.Create(cancelled.Id, employee, null);
        var liveAssignment = ShiftAssignment.Create(live.Id, employee, null);
        using (var writer = store.LightweightSession("own"))
        {
            writer.Store(deactivated);
            writer.Store(cancelled);
            writer.Store(live);
            writer.Store(staleOnDeactivated);
            writer.Store(staleOnCancelled);
            writer.Store(liveAssignment);
            writer.Store(new PartnerCleaningRequest { Id = cancelledRequest, Status = PartnerCleaningRequest.StatusCancelled });
            writer.Store(new PartnerCleaningRequest { Id = liveRequest, Status = PartnerCleaningRequest.StatusAccepted });
            await writer.SaveChangesAsync();
        }

        using var session = new TenantSessionFactory(store, Context("own").Object);
        var repository = new ShiftRepository(session);
        var items = await StaleAssignmentDryRun.Run(repository, async ids =>
        {
            using var statuses = store.LightweightSession("own");
            var requests = await statuses.LoadManyAsync<PartnerCleaningRequest>(ids);
            return requests.ToDictionary(r => r.Id, r => r.Status);
        });

        Assert.Equal(2, items.Count);
        Assert.Contains(items, i => i.AssignmentId == staleOnDeactivated.Id && i.Reason == "Shift is deactivated");
        Assert.Contains(items, i => i.AssignmentId == staleOnCancelled.Id && i.Reason == "Partner request is cancelled");
        Assert.DoesNotContain(items, i => i.AssignmentId == liveAssignment.Id);

        // Nothing was written: the stale assignments are still active.
        Assert.True((await repository.ListAssignmentsByShiftAsync(deactivated.Id)).Single().IsActive);
        Assert.True((await repository.ListAssignmentsByShiftAsync(cancelled.Id)).Single().IsActive);
    }
}
