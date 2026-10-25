using System.Net;
using System.Net.Http.Json;
using CleaningSuite.Api;
using CleaningSuite.Application.Bookings;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Bookings;
using CleaningSuite.Domain.Tenants;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Moq;

namespace CleaningSuite.Tests.Bookings;

public class PublicBookingCredentialHttpTests
{
    [Fact]
    public async Task Body_credentials_authorize_both_operations_and_old_phone_contract_is_removed()
    {
        using var host = new Host();
        using var oldLookup = await host.Client.GetAsync("/api/v1/public/synthetic/bookings/number?phone=123");
        Assert.Equal(HttpStatusCode.NotFound, oldLookup.StatusCode);
        using var phoneCancel = await host.Client.PostAsJsonAsync("/api/v1/public/synthetic/bookings/number/cancel?phone=123", new { phone = "123" });
        Assert.Equal(HttpStatusCode.NotFound, phoneCancel.StatusCode);
        using var lookup = await host.Client.PostAsJsonAsync("/api/v1/public/synthetic/bookings/lookup", new { bookingNumber = "number", managementKey = host.Key });
        Assert.Equal(HttpStatusCode.OK, lookup.StatusCode);
        Assert.True(lookup.Headers.CacheControl!.NoStore);
        var json = await lookup.Content.ReadAsStringAsync();
        Assert.DoesNotContain(host.Key, json);
        Assert.DoesNotContain(host.Booking.ManagementKeyHash!, json);
        using var cancel = await host.Client.PostAsJsonAsync("/api/v1/public/synthetic/bookings/number/cancel", new { managementKey = host.Key });
        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
        Assert.True(cancel.Headers.CacheControl!.NoStore);
        Assert.Equal(Booking.StatusCancelled, host.Booking.Status);
        Assert.DoesNotContain(host.Key, string.Join('\n', host.Logs));
        Assert.DoesNotContain(host.Booking.ManagementKeyHash!, string.Join('\n', host.Logs));
    }

    [Fact]
    public async Task Missing_wrong_malformed_legacy_and_cross_tenant_credentials_fail_without_disclosure()
    {
        foreach (var key in new string?[] { null, "123", "ABC123", new string('a', 64), new string('G', 64), new string('A', 1000), BookingManagementKey.Generate() })
        {
            using var host = new Host();
            foreach (var number in new[] { "number", "absent", "legacy" })
            {
                using var lookup = await host.Client.PostAsJsonAsync("/api/v1/public/synthetic/bookings/lookup", new { bookingNumber = number, managementKey = key });
                Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode);
                Assert.True(lookup.Headers.CacheControl!.NoStore);
                using var cancel = await host.Client.PostAsJsonAsync($"/api/v1/public/synthetic/bookings/{number}/cancel", new { managementKey = key });
                Assert.Equal(HttpStatusCode.NotFound, cancel.StatusCode);
                Assert.True(cancel.Headers.CacheControl!.NoStore);
                var error = await cancel.Content.ReadAsStringAsync();
                if (!string.IsNullOrEmpty(key)) Assert.DoesNotContain(key, error);
                Assert.DoesNotContain(number, error);
            }
            using var foreign = await host.Client.PostAsJsonAsync("/api/v1/public/other/bookings/lookup", new { bookingNumber = "number", managementKey = host.Key });
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
            host.Repository.Verify(x => x.SaveAsync(It.IsAny<Booking>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.DoesNotContain(host.Key, string.Join('\n', host.Logs));
        }
    }

    private sealed class Host : IDisposable
    {
        private readonly WebApplicationFactory<Program> factory;
        public string Key { get; } = BookingManagementKey.Generate();
        public Booking Booking { get; }
        public Mock<IBookingRepository> Repository { get; } = new();
        public HttpClient Client { get; }
        public System.Collections.Concurrent.ConcurrentQueue<string> Logs { get; } = new();

        public Host()
        {
            Booking = new() { BookingNumber = "number", ManagementKeyHash = BookingManagementKey.Hash(Key), Customer = new() { Phone = "123" }, CustomerReference = "ABC123" };
            var registry = new Mock<ITenantRegistry>();
            registry.Setup(x => x.GetBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string slug, CancellationToken _) => new TenantRegistration { Slug = slug, Status = TenantRegistration.StatusActive });
            factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureTestServices(services => {
                services.AddLogging(logging => logging.AddProvider(new CaptureLogs(Logs)));
                foreach (var descriptor in services.Where(x => x.ImplementationType == typeof(MartenSchemaInitializer) || x.ImplementationType == typeof(QualityCycleWorker)).ToList()) services.Remove(descriptor);
                services.RemoveAll<ITenantRegistry>(); services.AddSingleton(registry.Object);
                services.RemoveAll<IBookingRepository>();
                services.AddScoped(_ => {
                    var accessor = _.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
                    Repository.Setup(x => x.GetByNumberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync((string number, CancellationToken _) => accessor.HttpContext!.Items["TenantId"] as string != "synthetic" ? null : number == "number" ? Booking : number == "legacy" ? new Booking() : null);
                    return Repository.Object;
                });
            }));
            Client = factory.CreateClient();
        }

        public void Dispose() { Client.Dispose(); factory.Dispose(); }
    }

    private sealed class CaptureLogs(System.Collections.Concurrent.ConcurrentQueue<string> logs) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(logs);
        public void Dispose() { }
        private sealed class CaptureLogger(System.Collections.Concurrent.ConcurrentQueue<string> logs) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                logs.Enqueue(formatter(state, exception) + exception?.ToString());
        }
    }
}
