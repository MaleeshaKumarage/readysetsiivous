using CleaningSuite.Application.Employees;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Infrastructure.Persistence;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CleaningSuite.Tests.Shifts;

public class VisitAssignmentDatabaseTests
{
    private static readonly DateTime Start = new(2026, 10, 18, 8, 0, 0, DateTimeKind.Utc);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(Start); }
    private sealed class PauseReservation(IShiftWorkReservation inner) : IShiftWorkReservation
    {
        public readonly TaskCompletionSource Acquired = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Continue = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<IAsyncDisposable> AcquireAsync(CancellationToken ct = default)
        { var lease = await inner.AcquireAsync(ct); Acquired.SetResult(); await Continue.Task.WaitAsync(ct); return lease; }
    }
    private static Mock<ITenantContext> Context(string id) { var mock = new Mock<ITenantContext>(); mock.SetupGet(x => x.TenantId).Returns(id); return mock; }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Override_contains_queries_are_tenant_scoped_and_deletion_archives_roster()
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddCleaningMarten(Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!, databaseSchemaName: "visit_query_" + Guid.NewGuid().ToString("N"));
        using var provider = services.BuildServiceProvider(); var store = provider.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var shift = new Shift { Id = Guid.NewGuid(), IsActive = false }; var employee = Guid.NewGuid();
        var change = ShiftOccurrenceAssignment.Create(shift.Id, Start, null, OccurrenceAssignmentMode.Replace, [employee], "Cover");
        using (var writer = store.LightweightSession("own")) { writer.Store(shift); writer.Store(change); await writer.SaveChangesAsync(); }
        using (var writer = store.LightweightSession("foreign")) {
            writer.Store(ShiftOccurrenceAssignment.Create(shift.Id, Start.AddDays(1), null, OccurrenceAssignmentMode.Add, [employee], "Foreign")); await writer.SaveChangesAsync(); }
        using var session = new TenantSessionFactory(store, Context("own").Object); var repo = new ShiftRepository(session);
        Assert.Equal(change.Id, Assert.Single(await repo.ListOccurrenceAssignmentsByEmployeeAsync(employee)).Id);
        Assert.Single(await repo.ListOccurrenceAssignmentsAsync(shift.Id));
        await repo.DeleteAsync(shift.Id);
        Assert.Empty(await repo.ListOccurrenceAssignmentsByEmployeeAsync(employee));
        Assert.Equal(employee, Assert.Single(Assert.Single(await repo.ListOccurrenceAssignmentsAsync(shift.Id)).EmployeeIds));
        using var foreign = new TenantSessionFactory(store, Context("foreign").Object);
        Assert.Single(await new ShiftRepository(foreign).ListOccurrenceAssignmentsByEmployeeAsync(employee));
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public Task Clock_start_wins_and_reassignment_is_rejected() => Race(true);
    [QualityCycle.QualityCycleDatabaseFact]
    public Task Reassignment_wins_and_old_employee_cannot_start() => Race(false);
    private async Task Race(bool startWins)
    {
        var connection = Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!;
        var services = new ServiceCollection(); services.AddLogging();
        services.AddCleaningMarten(connection, databaseSchemaName: "visit_race_" + Guid.NewGuid().ToString("N"));
        using var provider = services.BuildServiceProvider(); var store = provider.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var tenant = "race-" + Guid.NewGuid().ToString("N");
        var shift = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Daily", new ShiftSchedule { Type = ShiftScheduleType.DailySameTime, DailyStart = TimeSpan.FromHours(8), DailyEnd = TimeSpan.FromHours(10) }, null, Start.Date, null, null);
        var a = new Employee { Id = Guid.NewGuid(), IsActive = true }; var b = new Employee { Id = Guid.NewGuid(), IsActive = true };
        var assignment = ShiftAssignment.Create(shift.Id, a.Id, null); assignment.AssignedAtUtc = Start.AddDays(-1);
        using (var writer = store.LightweightSession(tenant)) { writer.Store(shift); writer.Store(assignment); await writer.SaveChangesAsync(); }
        var employees = new Mock<IEmployeeRepository>(); employees.Setup(x => x.GetByIdAsync(a.Id, It.IsAny<CancellationToken>())).ReturnsAsync(a);
        employees.Setup(x => x.GetByIdAsync(b.Id, It.IsAny<CancellationToken>())).ReturnsAsync(b);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = connection }).Build();
        using var startSession = new TenantSessionFactory(store, Context(tenant).Object);
        using var assignSession = new TenantSessionFactory(store, Context(tenant).Object);
        var startRepo = new ShiftRepository(startSession); var assignRepo = new ShiftRepository(assignSession);
        var startReservation = new ShiftWorkReservation(configuration, startSession);
        var assignReservation = new ShiftWorkReservation(configuration, assignSession);
        var pause = new PauseReservation(startWins ? startReservation : assignReservation);
        var version = ShiftRosterReader.Version(await assignRepo.ListAssignmentHistoryAsync(shift.Id));
        var clock = new Clock();
        var starter = new QualityCycleHandlers.StartQualityCycleFormCommandHandler(startRepo, employees.Object, new QualityCycleRepository(startSession, store), startWins ? pause : startReservation, clock);
        var setter = new SetVisitAssigneesHandler(assignRepo, employees.Object, new QualityCycleRepository(assignSession, store), reservation: startWins ? assignReservation : pause, clock: clock);
        var command = new SetVisitAssigneesCommand(shift.Id, Start, null, OccurrenceAssignmentMode.Replace, [b.Id], "Cover", version);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        if (startWins)
        {
            var first = starter.Handle(new(shift.Id, a.Id, Start, Start.AddHours(2)), timeout.Token);
            await pause.Acquired.Task.WaitAsync(timeout.Token);
            var second = setter.Handle(command, timeout.Token);
            pause.Continue.SetResult(); await first;
            await Assert.ThrowsAsync<ShiftConflictException>(() => second);
        }
        else
        {
            var first = setter.Handle(command, timeout.Token);
            await pause.Acquired.Task.WaitAsync(timeout.Token);
            var second = starter.Handle(new(shift.Id, a.Id, Start, Start.AddHours(2)), timeout.Token);
            pause.Continue.SetResult(); await first;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => second);
        }
    }
}
