using CleaningSuite.Application.Bookings;
using CleaningSuite.Application.Bookings.Commands;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Bookings;
using CleaningSuite.Domain.Services;
using CleaningSuite.Domain.Tenants;
using CleaningSuite.Infrastructure.Persistence;
using CleaningSuite.Tests.QualityCycle;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CleaningSuite.Tests.Bookings;

[Collection("Database integration")]
public class BookingRecoveryDatabaseTests
{
    [QualityCycleDatabaseFact]
    public async Task Concurrent_lost_response_retries_create_one_booking_and_survive_a_new_session()
    {
        var connection = Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!;
        var services = new ServiceCollection(); services.AddLogging();
        services.AddTestMarten(connection, databaseSchemaName: "booking_recovery_" + Guid.NewGuid().ToString("N"));
        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var service = new Service { IsActive = true, DurationMinutes = 60, PriceNet = 50 };
        using (var writer = store.LightweightSession("booking-test")) { writer.Store(service); await writer.SaveChangesAsync(); }
        var context = new Mock<ITenantContext>(); context.SetupGet(x => x.TenantId).Returns("booking-test");
        var profiles = new Mock<ITenantProfileRepository>();
        profiles.Setup(x => x.GetAsync("booking-test", It.IsAny<CancellationToken>())).ReturnsAsync(new TenantProfile { TimeZoneId = "Europe/Helsinki" });
        var request = new CreatePublicBookingCommand("2030-01-15", "12:00", service.Id,
            "Synthetic customer", "123", "", "Street", "00100", "Helsinki", null, IdempotencyKey: new string('A', 64));
        async Task<PublicBookingResult> Send(CreatePublicBookingCommand command)
        {
            using var tenant = new TenantSessionFactory(store, context.Object);
            var bookings = new BookingRepository(tenant); var catalog = new ServiceRepository(tenant);
            var handler = new CreatePublicBookingHandler(context.Object, catalog, bookings, new AvailabilityEngine(catalog, bookings, profiles.Object));
            return await handler.Handle(command, default);
        }
        var results = await Task.WhenAll(Send(request), Send(request));
        Assert.Equal(results[0], results[1]);
        Assert.Equal(results[0], await Send(request));
        // A different booking needs its own key. 12:30 overlaps the 12:00-13:00 booking, so the slot conflict path runs.
        await Assert.ThrowsAsync<SlotConflictException>(() => Send(request with { StartTime = "12:30", IdempotencyKey = new string('B', 64) }));
        using var read = store.QuerySession("booking-test");
        var saved = Assert.Single(await read.Query<Booking>().ToListAsync());
        Assert.True(BookingManagementKey.Verify(results[0].ManagementKey, saved.ManagementKeyHash));
        Assert.DoesNotContain(request.IdempotencyKey!, System.Text.Json.JsonSerializer.Serialize(saved));
    }
}
