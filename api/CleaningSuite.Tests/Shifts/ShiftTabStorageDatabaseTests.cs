using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Infrastructure.Persistence;
using CleaningSuite.Tests.QualityCycle;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CleaningSuite.Tests.Shifts;

public class ShiftTabStorageDatabaseTests
{
    private static ShiftSchedule Weekly() => new() { Type = ShiftScheduleType.Weekly, WeeklyDays = [DayOfWeek.Monday], WeeklyStart = TimeSpan.FromHours(6), WeeklyEnd = TimeSpan.FromHours(8) };
    private static ShiftSchedule OneTime() => new() { Type = ShiftScheduleType.OneTime, OneTimeDate = new DateTime(2026, 11, 6), OneTimeStart = TimeSpan.FromHours(8), OneTimeEnd = TimeSpan.FromHours(10) };

    private static (IDocumentStore store, string partition) Setup(string schemaPrefix)
    {
        var connection = Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!;
        var services = new ServiceCollection(); services.AddLogging();
        services.AddCleaningMarten(connection, databaseSchemaName: schemaPrefix + Guid.NewGuid().ToString("N"));
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IDocumentStore>(), "tabs-" + Guid.NewGuid().ToString("N")[..8]);
    }

    private static TenantSessionFactory Session(IDocumentStore store, string partition)
    {
        var context = new Mock<ITenantContext>(); context.SetupGet(c => c.TenantId).Returns(partition);
        return new(store, context.Object);
    }

    [QualityCycleDatabaseFact]
    public async Task Stored_tabs_follow_the_list_rules_and_page_in_the_database()
    {
        var (store, partition) = Setup("shift_tabs_");
        var company = Guid.NewGuid(); var branch = Guid.NewGuid();
        var recurring = Shift.Create(company, branch, "Weekly clean", Weekly(), null, null, null);
        var visit = Shift.Create(company, branch, "Open visit", OneTime(), null, null, null);
        var done = Shift.Create(company, branch, "Done visit", OneTime(), null, null, null);
        var paused = Shift.Create(company, branch, "Old contract", Weekly(), null, null, null);
        paused.Deactivate();

        using (var session = Session(store, partition))
        {
            var shifts = new ShiftRepository(session);
            await shifts.SaveAsync(recurring); await shifts.SaveAsync(visit); await shifts.SaveAsync(done); await shifts.SaveAsync(paused);
            // Submitting the checklist of the done visit moves it to the Completed tab.
            var forms = new QualityCycleRepository(session, store);
            await forms.SaveFormAsync(new QualityCycleForm { ShiftId = done.Id, EmployeeId = Guid.NewGuid(), IsSubmitted = true, ShiftOccurrenceUtc = new DateTime(2026, 11, 6, 8, 0, 0, DateTimeKind.Utc) });
        }

        using var check = Session(store, partition);
        var read = new ShiftRepository(check);
        Assert.Equal(new ShiftTabCounts(2, 1, 1), await read.CountTabsAsync(company, null));
        var completed = await read.ListTabPageAsync(ShiftTabs.Completed, company, null, null, 0, 25);
        Assert.Equal(["Done visit"], completed.Items.Select(s => s.Name).ToArray());
        Assert.True(completed.Items[0].ChecklistCompleted);
        var search = await read.ListTabPageAsync(ShiftTabs.Active, company, null, "WEEKLY", 0, 25);
        Assert.Equal(["Weekly clean"], search.Items.Select(s => s.Name).ToArray());
        var second = await read.ListTabPageAsync(ShiftTabs.Active, company, null, null, 1, 1);
        Assert.Equal(2, second.Total);
        Assert.Equal(["Weekly clean"], second.Items.Select(s => s.Name).ToArray());
        Assert.Equal(new ShiftTabCounts(0, 0, 0), await read.CountTabsAsync(Guid.NewGuid(), null));
        Assert.Equal(ShiftTabs.Inactive, (await read.ListTabPageAsync(ShiftTabs.Inactive, company, null, null, 0, 25)).Items.Single().Tab);
    }

    [QualityCycleDatabaseFact]
    public async Task Shifts_saved_before_tabs_existed_get_their_tab_once()
    {
        var (store, partition) = Setup("shift_tab_backfill_");
        var company = Guid.NewGuid();
        var legacy = Shift.Create(company, Guid.NewGuid(), "Legacy clean", Weekly(), null, null, null);
        legacy.Tab = string.Empty;
        using (var writer = store.LightweightSession(partition)) { writer.Store(legacy); await writer.SaveChangesAsync(); }

        using var session = Session(store, partition);
        var shifts = new ShiftRepository(session);
        await shifts.EnsureTabsAsync();
        using var check = Session(store, partition);
        var stored = await new ShiftRepository(check).GetAsync(legacy.Id);
        Assert.Equal(ShiftTabs.Active, stored!.Tab);
        Assert.Equal(new ShiftTabCounts(1, 0, 0), await new ShiftRepository(check).CountTabsAsync(company, null));
    }
}
