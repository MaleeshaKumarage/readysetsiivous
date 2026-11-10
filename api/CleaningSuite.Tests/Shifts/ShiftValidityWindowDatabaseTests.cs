using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Infrastructure.Persistence;
using CleaningSuite.Tests.QualityCycle;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CleaningSuite.Tests.Shifts;

// Recreates the reported production path: a shift created for one month through the real create command,
// stored in Marten, then read for the months after its end. Nothing is mocked except the tenant id.
[Collection("Database integration")]
public class ShiftValidityWindowDatabaseTests
{
    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Shift_created_for_one_month_has_no_visits_on_the_roster_after_its_end_date()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTestMarten(Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!,
            databaseSchemaName: "shift_window_" + Guid.NewGuid().ToString("N"));
        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var tenant = "shift-window-" + Guid.NewGuid().ToString("N");
        var context = new Mock<ITenantContext>();
        context.SetupGet(x => x.TenantId).Returns(tenant);

        var company = Company.Create("1234567-8", "Owner company");
        company.IsPartnerEnabled = false;
        var branch = Branch.Create(company.Id, "Main branch"); branch.SetPropertyDetails(34, "Deep");
        using (var writer = store.LightweightSession(tenant)) { writer.Store(company); writer.Store(branch); await writer.SaveChangesAsync(); }

        using var session = new TenantSessionFactory(store, context.Object);
        var repository = new ShiftRepository(session);
        var handler = new ShiftHandlers.CreateShiftCommandHandler(repository, new CompanyRepository(session), new BranchRepository(session));
        var created = await handler.Handle(new CreateShiftCommand(company.Id, branch.Id, "Weekly office clean",
            new ShiftSchedule { Type = ShiftScheduleType.Weekly, WeeklyDays = [DayOfWeek.Monday], WeeklyStart = new(9, 0, 0), WeeklyEnd = new(10, 0, 0) },
            null, new DateTime(2026, 10, 1), new DateTime(2026, 10, 31)), default);
        var shift = await repository.GetAsync(created.Id, default);
        Assert.NotNull(shift);

        // Visits inside the month: Mondays 5, 12, 19 and 26 October (UTC here: no tenant profile is provisioned).
        var october = await repository.ResolveRosterAsync(shift!, new DateTime(2026, 10, 1), new DateTime(2026, 11, 1), TimeZoneInfo.Utc, default);
        Assert.Equal(4, october.Count);

        // The months after the end date must have no scheduled visits, on the roster or any single day.
        var november = await repository.ResolveRosterAsync(shift!, new DateTime(2026, 11, 1), new DateTime(2026, 12, 1), TimeZoneInfo.Utc, default);
        Assert.Empty(november);
        var firstMondayAfter = await repository.ResolveRosterAsync(shift!, new DateTime(2026, 11, 2), new DateTime(2026, 11, 3), TimeZoneInfo.Utc, default);
        Assert.Empty(firstMondayAfter);
    }
}
