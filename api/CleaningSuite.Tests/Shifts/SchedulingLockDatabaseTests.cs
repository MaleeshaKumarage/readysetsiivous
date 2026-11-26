using System.Diagnostics;
using CleaningSuite.Application;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Shifts;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Infrastructure.Persistence;
using CleaningSuite.Tests.QualityCycle;
using Marten;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace CleaningSuite.Tests.Shifts;

/// <summary>
/// The tenant scheduling lock against a real PostgreSQL database. A visit move takes the lock in the
/// pipeline (SchedulingWriteBehavior) and again in its handler; both must share one hold, or the request
/// waits on itself until the lock timeout and fails with "Scheduling is being updated. Reload and retry."
/// </summary>
[Collection("Database integration")]
public class SchedulingLockDatabaseTests
{
    private const string Busy = "Scheduling is being updated. Reload and retry.";
    private static readonly DateTime Start = new(2030, 3, 4, 8, 0, 0, DateTimeKind.Utc);

    private static async Task<(ServiceProvider Provider, string Tenant)> BuildAsync()
    {
        var tenant = "lock-" + Guid.NewGuid().ToString("N");
        var connection = Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!;
        var services = new ServiceCollection(); services.AddLogging();
        services.AddTestMarten(connection, databaseSchemaName: "lock_" + Guid.NewGuid().ToString("N"));
        var context = new Mock<ITenantContext>(); context.SetupGet(t => t.TenantId).Returns(tenant);
        services.AddSingleton(context.Object);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = connection }).Build());
        services.AddScoped<ITenantSession, TenantSessionFactory>();
        services.AddScoped<ITenantProfileRepository, TenantProfileRepository>();
        services.AddScoped<IShiftRepository, ShiftRepository>();
        services.AddScoped<IQualityCycleRepository, QualityCycleRepository>();
        services.AddScoped<IEmployeeRepository, EmployeeRepository>();
        services.AddScoped<IShiftWorkReservation, ShiftWorkReservation>();
        // The same pipeline as the API (Program.cs): the scheduling write behaviour wraps the handler.
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
            cfg.AddOpenBehavior(typeof(SchedulingWriteBehavior<,>));
        });
        var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IDocumentStore>().Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        return (provider, tenant);
    }

    [QualityCycleDatabaseFact]
    public async Task Moving_a_visit_through_the_pipeline_saves_instead_of_waiting_on_its_own_lock()
    {
        var (provider, tenant) = await BuildAsync();
        await using var _ = provider;
        var shift = Shift.Create(Guid.NewGuid(), Guid.NewGuid(), "Window clean", new ShiftSchedule { Type = ShiftScheduleType.OnCallFlexible }, null, null, null);
        var visit = ShiftOccurrenceException.Create(shift.Id, null, Start, Start.AddHours(1), ShiftOccurrenceExceptionStatus.Extra, "Added");
        using (var db = provider.GetRequiredService<IDocumentStore>().LightweightSession(tenant))
        {
            db.Store(shift); db.Store(visit);
            await db.SaveChangesAsync();
        }

        await using var scope = provider.CreateAsyncScope();
        var watch = Stopwatch.StartNew();
        var moved = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new SaveShiftOccurrenceExceptionCommand(
            shift.Id, visit.Id, null, Start.AddHours(2), Start.AddHours(3), ShiftOccurrenceExceptionStatus.Extra, "Moved in the employee calendar"));

        Assert.Equal(Start.AddHours(2), moved.StartUtc);
        Assert.Equal(Start.AddHours(3), moved.EndUtc);
        // Well under the 5-second lock timeout: the move never waited on itself.
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(4), $"The move took {watch.Elapsed}.");
    }

    [QualityCycleDatabaseFact]
    public async Task The_lock_is_shared_within_one_request_and_exclusive_between_requests()
    {
        var (provider, _) = await BuildAsync();
        await using var __ = provider;

        await using (var request = provider.CreateAsyncScope())
        {
            var reservation = request.ServiceProvider.GetRequiredService<IShiftWorkReservation>();
            await using (var outer = await reservation.AcquireAsync())
            {
                var watch = Stopwatch.StartNew();
                await using (var inner = await reservation.AcquireAsync()) { }
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"Re-entering took {watch.Elapsed}.");

                // Releasing the inner hold keeps the lock: another request is still refused.
                await using var other = provider.CreateAsyncScope();
                var refused = await Assert.ThrowsAsync<ShiftConflictException>(() =>
                    other.ServiceProvider.GetRequiredService<IShiftWorkReservation>().AcquireAsync());
                Assert.Equal(Busy, refused.Message);
            }
        }

        // Once the request has released it, another request takes the lock.
        await using var later = provider.CreateAsyncScope();
        await using (var lease = await later.ServiceProvider.GetRequiredService<IShiftWorkReservation>().AcquireAsync()) { }
    }
}
