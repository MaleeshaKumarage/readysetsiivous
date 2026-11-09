using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Domain.Tenants;
using CleaningSuite.Infrastructure.Persistence;
using CleaningSuite.Tests.QualityCycle;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CleaningSuite.Tests.Shifts;

[Collection("Database integration")]
public class ShiftValidityCorrectionDatabaseTests
{
    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Report_changes_nothing_and_apply_removes_only_planned_records_once_per_tenant()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTestMarten(Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!,
            databaseSchemaName: "shift_correction_" + Guid.NewGuid().ToString("N"));
        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var tenant = "correction-" + Guid.NewGuid().ToString("N");

        var shift = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Weekly clean",
            new ShiftSchedule { Type = ShiftScheduleType.Weekly, WeeklyDays = [DayOfWeek.Monday], WeeklyStart = new(9, 0, 0), WeeklyEnd = new(10, 0, 0) },
            null, new DateTime(2026, 10, 1), new DateTime(2026, 10, 31));
        var nov2 = new DateTime(2026, 11, 2, 6, 0, 0, DateTimeKind.Utc);
        var nov9 = new DateTime(2026, 11, 9, 6, 0, 0, DateTimeKind.Utc);
        var nov16 = new DateTime(2026, 11, 16, 6, 0, 0, DateTimeKind.Utc);
        var employee = Guid.NewGuid();
        var staleOverride = ShiftOccurrenceAssignment.Create(shift.Id, nov2, null, OccurrenceAssignmentMode.Replace, [employee], "Stale");
        var evidenceOverride = ShiftOccurrenceAssignment.Create(shift.Id, nov9, null, OccurrenceAssignmentMode.Replace, [employee], "Has work");
        var cancelled = ShiftOccurrenceException.Create(shift.Id, nov16, nov16, nov16.AddHours(1), ShiftOccurrenceExceptionStatus.CompanyCancelled, "Off", null);
        var extra = ShiftOccurrenceException.Create(shift.Id, null, new DateTime(2026, 11, 23, 6, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 23, 7, 0, 0, DateTimeKind.Utc),
            ShiftOccurrenceExceptionStatus.Extra, "Added", employee);
        var form = new QualityCycleForm { ShiftId = shift.Id, ShiftOccurrenceUtc = nov9, EmployeeId = employee };
        using (var writer = store.LightweightSession(tenant))
        {
            writer.Store(shift); writer.Store(staleOverride, evidenceOverride); writer.Store(cancelled, extra); writer.Store(form);
            await writer.SaveChangesAsync();
        }

        var registry = new Mock<ITenantRegistry>();
        registry.Setup(x => x.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([new TenantRegistration { Slug = tenant, Status = TenantRegistration.StatusActive }]);
        var runner = new ShiftValidityCorrectionRunner(store, registry.Object, NullLogger<ShiftValidityCorrectionRunner>.Instance);

        // Report only: the plan is produced, nothing is written and no marker is left.
        var report = await runner.RunAsync("Report", default);
        Assert.Equal(2, report.Single(x => x.Tenant == tenant).Removed);
        Assert.Equal(2, report.Single(x => x.Tenant == tenant).Reported);
        using (var reader = store.QuerySession(tenant))
        {
            Assert.Equal(2, await reader.Query<ShiftOccurrenceAssignment>().CountAsync());
            Assert.Equal(2, await reader.Query<ShiftOccurrenceException>().CountAsync());
            Assert.False(await reader.Query<DataCorrectionRecord>().AnyAsync());
        }

        // Apply: only the stale override and the cancellation beyond the end are removed; evidence, extra and moved visits stay.
        var applied = await runner.RunAsync("Apply", default);
        Assert.True(applied.Single(x => x.Tenant == tenant).Applied);
        using (var reader = store.QuerySession(tenant))
        {
            var overrides = await reader.Query<ShiftOccurrenceAssignment>().ToListAsync();
            Assert.DoesNotContain(overrides, x => x.Id == staleOverride.Id);
            Assert.Contains(overrides, x => x.Id == evidenceOverride.Id);
            var exceptions = await reader.Query<ShiftOccurrenceException>().ToListAsync();
            Assert.DoesNotContain(exceptions, x => x.Id == cancelled.Id);
            Assert.Contains(exceptions, x => x.Id == extra.Id);
            Assert.True(await reader.Query<DataCorrectionRecord>().AnyAsync());
        }

        // A later deployment with the marker present does nothing for this tenant.
        var again = await runner.RunAsync("Apply", default);
        Assert.DoesNotContain(again, x => x.Tenant == tenant);
    }
}
