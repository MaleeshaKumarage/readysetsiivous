using CleaningSuite.Domain.Shifts;
using CleaningSuite.Infrastructure.Persistence;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CleaningSuite.Tests.Shifts;

public class ShiftOccurrencePersistenceTests
{
    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Utc_occurrence_queries_preserve_range_boundaries_original_dates_and_tenant_scope()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCleaningMarten(Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!,
            databaseSchemaName: "occurrences_" + Guid.NewGuid().ToString("N"));
        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var tenant = "fixture-" + Guid.NewGuid().ToString("N");
        var shift = Guid.NewGuid();
        var employee = Guid.NewGuid();
        var from = new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddDays(7);
        var extra = ShiftOccurrenceException.Create(shift, null, from, from.AddHours(2),
            ShiftOccurrenceExceptionStatus.Extra, "At inclusive start", employee);
        var movedOut = ShiftOccurrenceException.Create(shift, from.AddDays(1), to.AddDays(2), to.AddDays(2).AddHours(2),
            ShiftOccurrenceExceptionStatus.Moved, "Original occurrence is in range", employee);
        var movedIn = ShiftOccurrenceException.Create(shift, from.AddDays(-1), from.AddDays(2), from.AddDays(2).AddHours(2),
            ShiftOccurrenceExceptionStatus.Moved, "Moved into range", employee);
        var atEnd = ShiftOccurrenceException.Create(shift, null, to, to.AddHours(2),
            ShiftOccurrenceExceptionStatus.Extra, "At exclusive end", employee);
        using (var writer = store.LightweightSession(tenant))
        {
            writer.Store(extra, movedOut, movedIn, atEnd);
            await writer.SaveChangesAsync();
        }
        using (var writer = store.LightweightSession(tenant + "-other"))
        {
            writer.Store(ShiftOccurrenceException.Create(shift, null, from, from.AddHours(2),
                ShiftOccurrenceExceptionStatus.Extra, "Foreign tenant", employee));
            await writer.SaveChangesAsync();
        }
        using var session = store.LightweightSession(tenant);
        var scoped = new Mock<ITenantSession>();
        scoped.SetupGet(x => x.Session).Returns(session);
        var repository = new ShiftRepository(scoped.Object);

        var byEmployee = await repository.ListOccurrenceExceptionsByEmployeeAsync(employee, from, to);
        Assert.Equal(new[] { extra.Id, movedIn.Id }.Order(), byEmployee.Select(x => x.Id).Order());
        var byShift = await repository.ListOccurrenceExceptionsAsync(shift, from, to);
        Assert.Equal(new[] { extra.Id, movedIn.Id, movedOut.Id }.Order(), byShift.Select(x => x.Id).Order());
        var original = await repository.GetOccurrenceExceptionByOriginalAsync(shift, movedOut.OriginalStartUtc!.Value);
        Assert.NotNull(original);
        Assert.Equal(movedOut.Id, original.Id);
        Assert.Equal(movedOut.StartUtc, original.StartUtc);
        Assert.Equal(DateTimeKind.Utc, original.StartUtc.Kind);
        Assert.Empty(await repository.ListOccurrenceExceptionsByEmployeeAsync(Guid.NewGuid(), from, to));
        Assert.Empty(await repository.ListOccurrenceExceptionsAsync(Guid.NewGuid(), from, to));
    }
}
