using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Npgsql;
using CleaningSuite.Application.Common;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.Employees.Commands;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Infrastructure.Keycloak;
using CleaningSuite.Infrastructure.Persistence;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CleaningSuite.Tests.QualityCycle;

public class QualityCycleWorkAccessDatabaseTests
{
    private static readonly DateTime Start = new(2026, 10, 18, 8, 0, 0, DateTimeKind.Utc);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(Start.AddDays(1)); }
    private static ITenantContext Context(string tenant)
    { var context = new Mock<ITenantContext>(); context.SetupGet(x => x.TenantId).Returns(tenant); return context.Object; }
    private sealed class PauseReservation(IShiftWorkReservation inner) : IShiftWorkReservation
    {
        public readonly TaskCompletionSource Acquired = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Continue = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<IAsyncDisposable> AcquireAsync(CancellationToken ct = default)
        {
            var lease = await inner.AcquireAsync(ct);
            try { Acquired.SetResult(); await Continue.Task.WaitAsync(ct); return lease; }
            catch { await lease.DisposeAsync(); throw; }
        }
    }

    private static async Task ObserveAdvisoryWaiterAsync(string connectionString, string tenant, CancellationToken ct)
    {
        var key = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes($"shift-work:{tenant}")));
        var unsigned = unchecked((ulong)key);
        await using var observer = new NpgsqlConnection(connectionString);
        await observer.OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1 FROM pg_locks l JOIN pg_stat_activity a ON a.pid = l.pid
                WHERE l.locktype = 'advisory' AND NOT l.granted AND l.objsubid = 1
                  AND l.classid::bigint = @high AND l.objid::bigint = @low
                  AND l.database = (SELECT oid FROM pg_database WHERE datname = current_database())
                  AND a.wait_event_type = 'Lock' AND a.wait_event = 'advisory')
            """, observer) { CommandTimeout = 2 };
        command.Parameters.AddWithValue("high", (long)(unsigned >> 32));
        command.Parameters.AddWithValue("low", (long)(unsigned & uint.MaxValue));
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(3))
        {
            if (await command.ExecuteScalarAsync(ct) is true) return;
            await Task.Delay(25, ct);
        }
        Assert.Fail("The losing operation never reached the exact tenant shift-work advisory lock wait.");
    }

    [QualityCycleDatabaseFact]
    public Task Deactivate_wins_against_start_submit_end_and_read_with_preloaded_sessions() => RaceAll(true, false);
    [QualityCycleDatabaseFact]
    public Task Update_inactive_wins_against_start_submit_end_and_read_with_preloaded_sessions() => RaceAll(true, true);
    [QualityCycleDatabaseFact]
    public Task Work_wins_before_deactivate_and_history_is_retained() => RaceAll(false, false);
    [QualityCycleDatabaseFact]
    public Task Work_wins_before_update_inactive_and_history_is_retained() => RaceAll(false, true);

    [QualityCycleDatabaseFact]
    public async Task Slow_identity_sync_releases_committed_work_reservation_and_preserves_concurrent_profile_fields()
    {
        var connection = Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!;
        var services = new ServiceCollection(); services.AddLogging();
        services.AddCleaningMarten(connection, databaseSchemaName: "qc_sync_" + Guid.NewGuid().ToString("N"));
        using var provider = services.BuildServiceProvider(); var store = provider.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = connection }).Build();
        foreach (var update in new[] { false, true })
        {
            var context = Context("sync-" + Guid.NewGuid().ToString("N"));
            var employee = new Employee { Email = "sync@example.invalid", KeycloakUserId = "synthetic-user", IsActive = true };
            using (var seed = store.LightweightSession(context.TenantId)) { seed.Store(employee); await seed.SaveChangesAsync(); }
            using var commandSession = new TenantSessionFactory(store, context);
            using var otherSession = new TenantSessionFactory(store, context);
            var employees = new EmployeeRepository(commandSession);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var identities = new Mock<IEmployeeIdentityProvider>();
            identities.Setup(x => x.SynchronizeAccessAsync(context.TenantId, employee.Id, employee.KeycloakUserId, employee.Role, false, It.IsAny<CancellationToken>()))
                .Returns(async () => { entered.SetResult(); await finish.Task; });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var identityLock = new EmployeeIdentityLock(configuration);
            var reservation = new ShiftWorkReservation(configuration, commandSession);
            var operation = update
                ? new UpdateEmployeeHandler(employees, identities.Object, context, identityLock, reservation).Handle(new(employee.Id,
                    new EmployeeFields(employee.Email, "Synthetic", "Cleaner", "", Employee.RoleEmployee, null, [], null, null, null, null, null), false), timeout.Token)
                : new DeactivateEmployeeHandler(employees, identities.Object, context, identityLock, reservation).Handle(new(employee.Id), timeout.Token);
            try
            {
                await entered.Task.WaitAsync(timeout.Token);
                Assert.False(operation.IsCompleted);
                // This real advisory acquisition would time out if provider I/O still held the work lock.
                await using (await new ShiftWorkReservation(configuration, otherSession).AcquireAsync(timeout.Token))
                {
                    var otherEmployees = new EmployeeRepository(otherSession);
                    var committed = (await otherEmployees.GetByIdAsync(employee.Id, timeout.Token))!;
                    Assert.False(committed.IsActive); Assert.True(committed.IdentitySyncPending);
                    committed.Notes = "Concurrent synthetic field";
                    await otherEmployees.SaveAsync(committed, timeout.Token);
                }
            }
            finally { finish.TrySetResult(); }
            await operation;
            using var verify = new TenantSessionFactory(store, context);
            var saved = (await new EmployeeRepository(verify).GetByIdAsync(employee.Id))!;
            Assert.False(saved.IsActive); Assert.False(saved.IdentitySyncPending);
            Assert.Equal("Concurrent synthetic field", saved.Notes);
        }
    }

    private static async Task RaceAll(bool deactivateWins, bool useUpdate)
    {
        var connection = Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!;
        var services = new ServiceCollection(); services.AddLogging();
        services.AddCleaningMarten(connection, databaseSchemaName: "qc_access_" + Guid.NewGuid().ToString("N"));
        using var provider = services.BuildServiceProvider(); var store = provider.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = connection }).Build();
        foreach (var operation in new[] { "start", "submit", "end", "read" })
        {
            var tenant = "synthetic-" + Guid.NewGuid().ToString("N"); var context = Context(tenant);
            // These isolated handlers omit tenant profile services, so the schedule uses UTC.
            var shift = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Synthetic daily", new ShiftSchedule
                { Type = ShiftScheduleType.DailySameTime, DailyStart = TimeSpan.FromHours(8), DailyEnd = TimeSpan.FromHours(10) }, null, Start.Date, null, null);
            var employee = new Employee { Email = "race@example.invalid", IsActive = true };
            var assignment = ShiftAssignment.Create(shift.Id, employee.Id, null); assignment.AssignedAtUtc = Start.AddDays(-1);
            var form = QualityCycleForm.Create(shift.Id, shift.Name, employee.Id, "Original owner", Guid.Empty, "Shift activities", Start, [], Start.AddHours(2));
            if (operation != "start") form.Start(Start);
            // Seed retained attachments on an in-progress document, then ensure a denied write never destroys them.
            form.Photos = [new("/synthetic.png", "Retained evidence")];
            using (var seed = store.LightweightSession(tenant)) { seed.Store(employee); seed.Store(shift); seed.Store(assignment); seed.Store(form); await seed.SaveChangesAsync(); }
            using var employeeSession = new TenantSessionFactory(store, context);
            using var workSession = new TenantSessionFactory(store, context);
            var employees = new EmployeeRepository(employeeSession); var workEmployees = new EmployeeRepository(workSession);
            var forms = new QualityCycleRepository(workSession, store); var shifts = new ShiftRepository(workSession);
            // Simulate middleware/prehandler reads made before the winning request commits.
            Assert.True((await workEmployees.GetByIdAsync(employee.Id))!.IsActive);
            Assert.NotNull(await forms.GetFormAsync(form.Id));
            Assert.True((await employees.GetByIdAsync(employee.Id))!.IsActive);
            var workReservation = new ShiftWorkReservation(configuration, workSession);
            var employeeReservation = new ShiftWorkReservation(configuration, employeeSession);
            var pause = new PauseReservation(deactivateWins ? employeeReservation : workReservation);
            IShiftWorkReservation workLock = deactivateWins ? workReservation : pause;
            IShiftWorkReservation employeeLock = deactivateWins ? pause : employeeReservation;
            var identityLock = new EmployeeIdentityLock(configuration);
            var identities = Mock.Of<IEmployeeIdentityProvider>();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            Task Disable() => useUpdate
                ? new UpdateEmployeeHandler(employees, identities, context, identityLock, employeeLock).Handle(new(employee.Id,
                    new EmployeeFields(employee.Email, "Synthetic", "Cleaner", "", Employee.RoleEmployee, null, [], null, null, null, null, null), false), timeout.Token)
                : new DeactivateEmployeeHandler(employees, identities, context, identityLock, employeeLock).Handle(new(employee.Id), timeout.Token);
            Task Work() => operation switch
            {
                "start" => new QualityCycleHandlers.StartQualityCycleFormCommandHandler(shifts, workEmployees, forms, workLock, new Clock()).Handle(new(shift.Id, employee.Id, Start, Start.AddHours(2)), timeout.Token),
                "submit" => new QualityCycleHandlers.SubmitQualityCycleFormCommandHandler(forms, NoopUploadContentValidator.Instance, shifts, workEmployees, workLock, new Clock()).Handle(new(form.Token, [], Photos: [new("/synthetic.png", "Retained evidence")]), timeout.Token),
                "end" => new QualityCycleHandlers.EndQualityCycleFormCommandHandler(forms, NoopUploadContentValidator.Instance, shifts, workEmployees, workLock, new Clock()).Handle(new(form.Id, employee.Id, [], Photos: [new("/synthetic.png", "Retained evidence")]), timeout.Token),
                _ => new QualityCycleHandlers.GetQualityCycleFormByTokenQueryHandler(forms, shifts, workEmployees, workLock).Handle(new(form.Token), timeout.Token)
            };
            var first = deactivateWins ? Disable() : Work();
            await pause.Acquired.Task.WaitAsync(timeout.Token);
            var second = deactivateWins ? Work() : Disable();
            try
            {
                await ObserveAdvisoryWaiterAsync(connection, tenant, timeout.Token);
                Assert.False(second.IsCompleted);
            }
            finally { pause.Continue.TrySetResult(); }
            await first;
            if (deactivateWins) await Assert.ThrowsAsync<UnauthorizedAccessException>(() => second);
            else await second;
            using var verifySession = new TenantSessionFactory(store, context);
            var saved = (await new QualityCycleRepository(verifySession, store).GetFormAsync(form.Id))!;
            Assert.False((await new EmployeeRepository(verifySession).GetByIdAsync(employee.Id))!.IsActive);
            Assert.Equal("Retained evidence", Assert.Single(saved.Photos).Note);
            Assert.Equal("Original owner", saved.EmployeeName); Assert.Equal(Start, saved.ShiftOccurrenceUtc);
            Assert.Equal(!deactivateWins && operation is "submit" or "end", saved.IsSubmitted);
            if (operation == "start") Assert.Equal(!deactivateWins, saved.StartedAtUtc.HasValue);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new QualityCycleHandlers.GetQualityCycleFormByTokenQueryHandler(forms, shifts, workEmployees, workReservation).Handle(new(form.Token), timeout.Token));
            // A second tenant cannot resolve either bearer token or form id.
            using var foreignSession = new TenantSessionFactory(store, Context(tenant + "-foreign"));
            var foreignForms = new QualityCycleRepository(foreignSession, store);
            var foreignReservation = new ShiftWorkReservation(configuration, foreignSession);
            var foreignEmployees = new EmployeeRepository(foreignSession); var foreignShifts = new ShiftRepository(foreignSession);
            await Assert.ThrowsAsync<NotFoundException>(() => new QualityCycleHandlers.GetQualityCycleFormByTokenQueryHandler(foreignForms, foreignShifts, foreignEmployees, foreignReservation).Handle(new(form.Token), timeout.Token));
            await Assert.ThrowsAsync<NotFoundException>(() => new QualityCycleHandlers.EndQualityCycleFormCommandHandler(foreignForms, NoopUploadContentValidator.Instance, foreignShifts, foreignEmployees, foreignReservation).Handle(new(form.Id, employee.Id, []), timeout.Token));
        }
    }
}
