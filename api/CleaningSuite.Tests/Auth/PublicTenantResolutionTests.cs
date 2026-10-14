using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using CleaningSuite.Api;
using CleaningSuite.Application.Agreements.Commands;
using CleaningSuite.Application.Agreements.Queries;
using CleaningSuite.Application.Bookings;
using CleaningSuite.Application.Bookings.Commands;
using CleaningSuite.Application.Bookings.Queries;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.QualityCycle;
using CleaningSuite.Application.Services.Queries;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Application.Tenants.Queries;
using CleaningSuite.Domain.Tenants;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace CleaningSuite.Tests.Auth;

public class PublicTenantResolutionTests
{
    private static readonly (string Method, string Route, string? Body)[] Routes = [
        ("GET", "services", null), ("GET", "content", null),
        ("GET", "availability?date=2026-10-14&serviceId=11111111-1111-1111-1111-111111111111", null),
        ("POST", "bookings", "{\"localDate\":\"2026-10-14\",\"startTime\":\"10:00\",\"serviceId\":\"11111111-1111-1111-1111-111111111111\",\"customerName\":\"Test\",\"customerPhone\":\"123\",\"customerEmail\":\"test@example.invalid\",\"street\":\"Street\",\"postalCode\":\"00100\",\"city\":\"Helsinki\"}"),
        ("GET", "bookings/number?phone=123", null), ("POST", "bookings/number/cancel?phone=123", null),
        ("GET", "quality-cycle/token", null),
        ("POST", "quality-cycle/token/submit", "{\"token\":\"token\",\"items\":[]}"),
        ("GET", "quality-cycle/reports/token/pdf", null),
        ("GET", "agreements/token", null),
        ("POST", "agreements/token/sign", "{\"typedName\":\"Test\",\"signaturePng\":\"AA==\"}"),
        ("GET", "agreements/token/file", null),
    ];

    [Theory]
    [InlineData(null)] [InlineData("tenant-b")]
    public async Task Every_public_family_dispatches_in_the_URL_tenant(string? realm)
    {
        using var host = new Host();
        foreach (var route in Routes)
        {
            host.Mediator.Invocations.Clear(); host.SeenTenant = host.SeenSlug = host.SeenRealm = null;
            using var response = await host.Send("tenant-b", realm, route);
            Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created or HttpStatusCode.NoContent or HttpStatusCode.NotFound,
                $"{route.Route}: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
            Assert.Single(host.Mediator.Invocations);
            Assert.Equal("tenant-b", host.SeenTenant); Assert.Equal("tenant-b", host.SeenSlug);
            Assert.Equal(realm, host.SeenRealm);
        }
    }

    [Theory]
    [InlineData("tenant-a")] [InlineData("master")]
    public async Task Foreign_authenticated_realm_never_dispatches_any_public_handler(string realm)
    {
        using var host = new Host();
        foreach (var route in Routes)
        {
            using var response = await host.Send("tenant-b", realm, route);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var problem = await response.Content.ReadAsStringAsync();
            Assert.Contains("tenant-mismatch", problem);
            Assert.DoesNotContain("tenant-a", problem); Assert.DoesNotContain("tenant-b", problem);
        }
        Assert.Empty(host.Mediator.Invocations); Assert.Empty(host.Employees.Invocations);
    }

    [Theory]
    [InlineData(null, "unknown")] [InlineData("unknown", "unknown")]
    [InlineData(null, "inactive")] [InlineData("inactive", "inactive")]
    [InlineData("tenant-a", "inactive")]
    public async Task Unknown_or_inactive_URL_tenant_never_dispatches(string? realm, string slug)
    {
        using var host = new Host();
        foreach (var route in Routes)
        {
            using var response = await host.Send(slug, realm, route);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        Assert.Empty(host.Mediator.Invocations); Assert.Empty(host.Employees.Invocations);
    }

    [Fact]
    public async Task Public_prefix_is_anchored()
    {
        using var host = new Host();
        using var response = await host.Client.GetAsync("/unrelated/api/v1/public/tenant-b/services");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(host.Registry.Invocations); Assert.Empty(host.Mediator.Invocations);
    }

    private sealed class Host : IDisposable
    {
        private readonly RSA rsa = RSA.Create(2048);
        private readonly RsaSecurityKey key;
        private readonly WebApplicationFactory<Program> factory;
        private readonly HttpContextAccessor accessor = new();
        public Mock<IMediator> Mediator { get; } = new();
        public Mock<IEmployeeRepository> Employees { get; } = new();
        public Mock<ITenantRegistry> Registry { get; } = new();
        public HttpClient Client { get; }
        public string? SeenTenant, SeenSlug, SeenRealm;

        public Host()
        {
            key = new RsaSecurityKey(rsa) { KeyId = "public-test-key" };
            var parameters = rsa.ExportParameters(false);
            var publicKey = new { kty = "RSA", kid = key.KeyId, use = "sig", alg = "RS256",
                n = Base64UrlEncoder.Encode(parameters.Modulus!), e = Base64UrlEncoder.Encode(parameters.Exponent!) };
            Registry.Setup(x => x.GetBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string slug, CancellationToken _) => slug == "unknown" ? null : new TenantRegistration {
                    Slug = slug, Status = slug == "inactive" ? TenantRegistration.StatusSuspended : TenantRegistration.StatusActive });
            Setup<GetPublicServicesQuery, IReadOnlyList<PublicServiceDto>>(Array.Empty<PublicServiceDto>());
            Setup<GetPublicContentQuery, PublicContentDto?>(null);
            Setup<GetAvailabilityQuery, IReadOnlyList<AvailabilitySlot>>(Array.Empty<AvailabilitySlot>());
            Setup<CreatePublicBookingCommand, PublicBookingResult>(new(Guid.NewGuid(), "number", "reference"));
            Setup<GetPublicBookingQuery, PublicBookingDto?>(null);
            Setup<CancelPublicBookingCommand, Unit>(Unit.Value);
            Setup<GetQualityCycleFormByTokenQuery, QualityCycleFormDto>(null!);
            Setup<SubmitQualityCycleFormCommand, QualityCycleFormDto>(null!);
            Setup<GetSharedQualityCycleReportPdfQuery, byte[]>("%PDF-test"u8.ToArray());
            Setup<GetPublicAgreementQuery, PublicAgreementDto?>(null);
            Setup<SignAgreementCommand, SignResult>(new(true, 1, 1));
            Setup<GetAgreementFileQuery, string?>(null);
            factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => {
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> {
                    ["Auth:Keycloak:ServerUrl"] = "https://auth.test", ["Auth:Keycloak:Audience"] = "cleaning-suite-api",
                    ["Auth:Keycloak:PlatformAudience"] = "cleaning-suite-platform-api", ["Auth:Keycloak:ProvisionerRealm"] = "master" }));
                builder.ConfigureTestServices(services => {
                    foreach (var descriptor in services.Where(x => x.ImplementationType == typeof(MartenSchemaInitializer) || x.ImplementationType == typeof(QualityCycleWorker)).ToList()) services.Remove(descriptor);
                    services.RemoveAll<ITenantRegistry>(); services.AddSingleton(Registry.Object);
                    services.RemoveAll<IEmployeeRepository>(); services.AddSingleton(Employees.Object);
                    services.RemoveAll<IMediator>(); services.AddSingleton(Mediator.Object);
                    services.RemoveAll<IHttpContextAccessor>(); services.AddSingleton<IHttpContextAccessor>(accessor);
                    services.AddHttpClient("KeycloakSigningKeys").ConfigurePrimaryHttpMessageHandler(() => new Jwks(JsonSerializer.Serialize(new { keys = new[] { publicKey } })));
                });
            });
            Client = factory.CreateClient();
        }

        private void Setup<TRequest, TResponse>(TResponse response) where TRequest : class, IRequest<TResponse> =>
            Mediator.Setup(x => x.Send(It.IsAny<TRequest>(), It.IsAny<CancellationToken>())).Callback(() => {
                SeenTenant = accessor.HttpContext!.Items["TenantId"] as string;
                SeenSlug = accessor.HttpContext.Items["Slug"] as string;
                SeenRealm = accessor.HttpContext.Items["Realm"] as string;
            }).ReturnsAsync(response);

        public Task<HttpResponseMessage> Send(string slug, string? realm, (string Method, string Route, string? Body) route)
        {
            var request = new HttpRequestMessage(new HttpMethod(route.Method), $"/api/v1/public/{slug}/{route.Route}");
            if (realm is not null)
            {
                var token = new JwtSecurityToken("https://auth.test/realms/" + realm,
                    realm == "master" ? "cleaning-suite-platform-api" : "cleaning-suite-api",
                    [new Claim("sub", "public-sub")], DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(20),
                    new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
                request.Headers.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
            }
            if (route.Body is not null) request.Content = new StringContent(route.Body, System.Text.Encoding.UTF8, "application/json");
            return Client.SendAsync(request);
        }

        public void Dispose() { Client.Dispose(); factory.Dispose(); rsa.Dispose(); }
    }

    private sealed class Jwks(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
    }
}
