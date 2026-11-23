using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Tenants;
using CleaningSuite.Infrastructure.Persistence;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CleaningSuite.Tests.Shifts;

[Collection("Database integration")]
public class StartupRepairGuardDatabaseTests
{
    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Branch_shift_merge_applies_once_then_is_skipped_on_later_starts()
    {
        var tenant = "guard-merge-" + Guid.NewGuid().ToString("N");
        var services = new ServiceCollection(); services.AddLogging();
        services.AddTestMarten(Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!, databaseSchemaName: "guard_merge_" + Guid.NewGuid().ToString("N"));
        using var provider = services.BuildServiceProvider(); var store = provider.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var registry = new Mock<ITenantRegistry>();
        registry.Setup(x => x.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([new TenantRegistration { Slug = tenant, Status = TenantRegistration.StatusActive }]);
        var runner = new BranchShiftMergeRunner(store, registry.Object, new ConfigurationBuilder().Build(), NullLogger<BranchShiftMergeRunner>.Instance);

        var first = await runner.RunAsync("Apply", default);
        Assert.Single(first);
        Assert.Equal(0, first[0].Failed);
        Assert.True(await StartupRepairGuard.IsCompleteAsync(store, tenant, StartupRepairMarker.BranchShiftMerge, default));

        // The second start finds the marker and does no work at all.
        Assert.Empty(await runner.RunAsync("Apply", default));
    }
}
