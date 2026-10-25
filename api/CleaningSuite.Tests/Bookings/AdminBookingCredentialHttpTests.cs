using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleaningSuite.Api;
using CleaningSuite.Application.Bookings;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Bookings;
using CleaningSuite.Domain.Common;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Tenants;
using CleaningSuite.Infrastructure.Persistence;
using Marten;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace CleaningSuite.Tests.Bookings;

public class AdminBookingCredentialHttpTests
{
    [Theory]
    [InlineData("list")] [InlineData("detail")] [InlineData("schedule")]
    public async Task Signed_admin_booking_responses_preserve_allowlisted_contract_without_credentials(string route)
    {
        using var host = new Host();
        var path = route switch
        {
            "list" => "/api/v1/admin/bookings",
            "detail" => $"/api/v1/admin/bookings/{host.Booking.Id}",
            _ => $"/api/v1/admin/schedule?date={host.Booking.StartLocalDate}&employeeId={host.Employee.Id}"
        };
        using var response = await host.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(host.Key, body);
        Assert.DoesNotContain(host.Booking.ManagementKeyHash!, body);
        Assert.DoesNotContain("managementKey", body, StringComparison.OrdinalIgnoreCase);
        using var json = JsonDocument.Parse(body);
        var item = route == "detail" ? json.RootElement : Assert.Single(json.RootElement.EnumerateArray());
        Assert.Equal(new[] { "id", "createdUtc", "updatedUtc", "bookingNumber", "customer", "cleaningAddress",
            "serviceId", "service", "employeeId", "employeeName", "startUtc", "endUtc", "startLocalDate",
            "startLocalTime", "total", "status", "source", "customerReference", "notes", "history" }.Order(),
            item.EnumerateObject().Select(x => x.Name).Order());
        Assert.Equal(host.Booking.Id, item.GetProperty("id").GetGuid());
        Assert.Equal(host.Booking.BookingNumber, item.GetProperty("bookingNumber").GetString());
        Assert.Equal(host.Booking.CustomerReference, item.GetProperty("customerReference").GetString());
        Assert.Equal(host.Booking.Customer.Email, item.GetProperty("customer").GetProperty("email").GetString());
        Assert.Equal(host.Booking.CleaningAddress.PostalCode, item.GetProperty("cleaningAddress").GetProperty("postalCode").GetString());
        Assert.Equal(host.Booking.Service.NameEn, item.GetProperty("service").GetProperty("nameEn").GetString());
        Assert.Equal(host.Booking.Total.Gross, item.GetProperty("total").GetProperty("gross").GetDecimal());
        Assert.Equal(host.Booking.Notes, item.GetProperty("notes").GetString());
        Assert.Equal("Synthetic history", Assert.Single(item.GetProperty("history").EnumerateArray()).GetProperty("note").GetString());
        // Response mapping must never erase persistence data or use JsonIgnore on the domain model.
        var storedJson = Newtonsoft.Json.JsonConvert.SerializeObject(host.Booking);
        Assert.Contains(host.Booking.ManagementKeyHash!, storedJson);
        Assert.DoesNotContain(host.Key, storedJson);
        var roundtrip = Newtonsoft.Json.JsonConvert.DeserializeObject<Booking>(storedJson)!;
        Assert.True(BookingManagementKey.Verify(host.Key, roundtrip.ManagementKeyHash));
        host.Repository.Verify(x => x.SaveAsync(It.IsAny<Booking>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [QualityCycle.QualityCycleDatabaseFact]
    public async Task Repository_roundtrip_retains_digest_and_never_stores_raw_key()
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddCleaningMarten(Environment.GetEnvironmentVariable("QC_TEST_CONNECTION_STRING")!, databaseSchemaName: "booking_key_" + Guid.NewGuid().ToString("N"));
        using var provider = services.BuildServiceProvider(); var store = provider.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var context = new Mock<ITenantContext>(); context.SetupGet(x => x.TenantId).Returns("synthetic-" + Guid.NewGuid().ToString("N"));
        var key = BookingManagementKey.Generate();
        var booking = new Booking { BookingNumber = "synthetic", ManagementKeyHash = BookingManagementKey.Hash(key) };
        using (var writer = new TenantSessionFactory(store, context.Object))
            await new BookingRepository(writer).SaveAsync(booking);
        using var reader = new TenantSessionFactory(store, context.Object);
        var saved = await new BookingRepository(reader).GetByIdAsync(booking.Id);
        Assert.NotNull(saved);
        Assert.Equal(booking.ManagementKeyHash, saved.ManagementKeyHash);
        Assert.True(BookingManagementKey.Verify(key, saved.ManagementKeyHash));
        var persistedJson = Newtonsoft.Json.JsonConvert.SerializeObject(saved);
        Assert.Contains(booking.ManagementKeyHash!, persistedJson);
        Assert.DoesNotContain(key, persistedJson);
    }

    private sealed class Host : IDisposable
    {
        private readonly RSA rsa = RSA.Create(2048);
        private readonly WebApplicationFactory<Program> factory;
        public string Key { get; } = BookingManagementKey.Generate();
        public Employee Employee { get; } = new() { KeycloakUserId = "synthetic-admin", Role = "admin" };
        public Booking Booking { get; }
        public Mock<IBookingRepository> Repository { get; } = new();
        public HttpClient Client { get; }

        public Host()
        {
            Booking = new() { BookingNumber = "RSS-2030-000001", ManagementKeyHash = BookingManagementKey.Hash(Key),
                CustomerReference = "ABC123", EmployeeId = Employee.Id.ToString(), EmployeeName = "Synthetic admin",
                Customer = new() { Name = "Synthetic customer", Phone = "123", Email = "synthetic@example.invalid" },
                CleaningAddress = new() { Street = "Synthetic street", PostalCode = "00100", City = "Helsinki" },
                Service = new() { NameEn = "Synthetic cleaning", DurationMinutes = 60 }, Total = Money.FromNet(50, 25.5m),
                StartUtc = new(2030, 1, 15, 10, 0, 0, DateTimeKind.Utc), EndUtc = new(2030, 1, 15, 11, 0, 0, DateTimeKind.Utc),
                StartLocalDate = "2030-01-15", StartLocalTime = "12:00", Notes = "Synthetic note",
                History = [new() { Status = CleaningSuite.Domain.Bookings.Booking.StatusNew, By = "customer", Note = "Synthetic history" }] };
            Repository.Setup(x => x.ListAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync([Booking]);
            Repository.Setup(x => x.GetByIdAsync(Booking.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Booking);
            Repository.Setup(x => x.ListForLocalDateAsync(Booking.StartLocalDate, It.IsAny<CancellationToken>())).ReturnsAsync([Booking]);
            var employees = new Mock<IEmployeeRepository>();
            employees.Setup(x => x.GetByKeycloakUserIdAsync(Employee.KeycloakUserId, It.IsAny<CancellationToken>())).ReturnsAsync(Employee);
            var registry = new Mock<ITenantRegistry>();
            registry.Setup(x => x.GetBySlugAsync("synthetic", It.IsAny<CancellationToken>())).ReturnsAsync(new TenantRegistration { Slug = "synthetic", Status = TenantRegistration.StatusActive });
            var key = new RsaSecurityKey(rsa) { KeyId = Guid.NewGuid().ToString("N") };
            var publicKey = rsa.ExportParameters(false);
            var jwks = JsonSerializer.Serialize(new { keys = new[] { new { kty = "RSA", kid = key.KeyId, use = "sig", alg = "RS256",
                n = Base64UrlEncoder.Encode(publicKey.Modulus!), e = Base64UrlEncoder.Encode(publicKey.Exponent!) } } });
            factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => {
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> {
                    ["Auth:Keycloak:ServerUrl"] = "https://auth.test", ["Auth:Keycloak:Audience"] = "cleaning-suite-api" }));
                builder.ConfigureTestServices(services => {
                    foreach (var descriptor in services.Where(x => x.ImplementationType == typeof(MartenSchemaInitializer) || x.ImplementationType == typeof(QualityCycleWorker)).ToList()) services.Remove(descriptor);
                    services.RemoveAll<IBookingRepository>(); services.AddSingleton(Repository.Object);
                    services.RemoveAll<IEmployeeRepository>(); services.AddSingleton(employees.Object);
                    services.RemoveAll<ITenantRegistry>(); services.AddSingleton(registry.Object);
                    services.AddHttpClient("KeycloakSigningKeys").ConfigurePrimaryHttpMessageHandler(() => new Keys(jwks));
                });
            });
            Client = factory.CreateClient();
            var jwt = new JwtSecurityToken("https://auth.test/realms/synthetic", "cleaning-suite-api", [
                new Claim("sub", Employee.KeycloakUserId), new Claim("email_verified", "true"),
                new Claim("realm_access", JsonSerializer.Serialize(new { roles = new[] { Employee.Role } }))
            ], DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(5), new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
            Client.DefaultRequestHeaders.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(jwt));
        }

        public void Dispose() { Client.Dispose(); factory.Dispose(); rsa.Dispose(); }
    }

    private sealed class Keys(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }
}
