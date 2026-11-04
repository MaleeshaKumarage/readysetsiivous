using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Infrastructure.Persistence;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CleaningSuite.Tests.Shifts;

/// <summary>The batched lookup behind the shift list's Completed tab, read on PostgreSQL.</summary>
public class SubmittedFormLookupDatabaseTests
{
    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Returns_only_requested_shifts_with_a_submitted_checklist_on_a_live_shift()
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddCleaningMarten(Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!,
            databaseSchemaName: "submitted_forms_" + Guid.NewGuid().ToString("N"));
        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var tenant = "forms-" + Guid.NewGuid().ToString("N"); var other = "forms-other-" + Guid.NewGuid().ToString("N");
        Guid done = Guid.NewGuid(), open = Guid.NewGuid(), deleted = Guid.NewGuid(), notAsked = Guid.NewGuid(), foreign = Guid.NewGuid();
        QualityCycleForm Form(Guid shift, bool submitted, DateTime? shiftDeleted = null) => new()
        {
            ShiftId = shift, EmployeeId = Guid.NewGuid(), Token = Guid.NewGuid().ToString("N"),
            ShiftOccurrenceUtc = new DateTime(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc), IsSubmitted = submitted, ShiftDeletedUtc = shiftDeleted,
        };
        using (var db = store.LightweightSession(tenant))
        {
            db.Store(Form(done, true), Form(done, false), Form(open, false), Form(deleted, true, DateTime.UtcNow), Form(notAsked, true));
            await db.SaveChangesAsync();
        }
        using (var db = store.LightweightSession(other)) { db.Store(Form(foreign, true)); await db.SaveChangesAsync(); }

        var context = new Mock<ITenantContext>(); context.SetupGet(x => x.TenantId).Returns(tenant);
        using var session = new TenantSessionFactory(store, context.Object);
        var repository = new QualityCycleRepository(session, store);

        var result = await repository.ListShiftIdsWithSubmittedFormAsync([done, open, deleted, foreign]);

        Assert.Equal(done, Assert.Single(result));
        Assert.Empty(await repository.ListShiftIdsWithSubmittedFormAsync([]));
    }
}
