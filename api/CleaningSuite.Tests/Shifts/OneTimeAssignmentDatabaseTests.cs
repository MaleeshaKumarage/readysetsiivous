using CleaningSuite.Application.Common;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Infrastructure.Persistence;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CleaningSuite.Tests.Shifts;

public class OneTimeAssignmentDatabaseTests
{
    private static Mock<ITenantContext> Context(string id) { var mock = new Mock<ITenantContext>(); mock.SetupGet(x => x.TenantId).Returns(id); return mock; }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task A_cleaner_can_be_assigned_to_a_one_time_shift_once()
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddCleaningMarten(Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!, databaseSchemaName: "one_time_assign_" + Guid.NewGuid().ToString("N"));
        using var provider = services.BuildServiceProvider(); var store = provider.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var tenant = "one-time-" + Guid.NewGuid().ToString("N");
        var day = new DateTime(2030, 1, 7);
        var shift = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "One-time visit",
            new ShiftSchedule { Type = ShiftScheduleType.OneTime, OneTimeDate = day, OneTimeStart = TimeSpan.FromHours(8), OneTimeEnd = TimeSpan.FromHours(10) },
            null, day, day, null);
        var cleaner = Guid.NewGuid();
        using (var writer = store.LightweightSession(tenant)) { writer.Store(shift); await writer.SaveChangesAsync(); }

        using var session = new TenantSessionFactory(store, Context(tenant).Object);
        var repository = new ShiftRepository(session);
        var employees = new Mock<IEmployeeRepository>(MockBehavior.Loose) { DefaultValue = DefaultValue.Empty };
        employees.Setup(x => x.GetByIdAsync(cleaner, It.IsAny<CancellationToken>())).ReturnsAsync(new Employee { Id = cleaner, IsActive = true });
        var profiles = new Mock<ITenantProfileRepository>(MockBehavior.Loose);
        profiles.Setup(x => x.GetAsync(tenant, It.IsAny<CancellationToken>())).ReturnsAsync(new CleaningSuite.Domain.Tenants.TenantProfile { TimeZoneId = "Europe/Helsinki" });
        var handler = new ShiftHandlers.AssignEmployeeToShiftCommandHandler(repository, employees.Object, new Mock<IQualityCycleRepository>(MockBehavior.Loose) { DefaultValue = DefaultValue.Empty }.Object,
            new Mock<IEmailSender>(MockBehavior.Loose).Object, 180, null, profiles.Object, Context(tenant).Object);

        var assigned = await handler.Handle(new AssignEmployeeToShiftCommand(shift.Id, cleaner), default);
        Assert.Equal(cleaner, assigned.EmployeeId);
        var active = (await repository.ListAssignmentsByShiftAsync(shift.Id, default)).Where(a => a.IsActive).ToArray();
        Assert.Single(active);
    }
}
