using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using CleaningSuite.Api;
using CleaningSuite.Api.Auth;
using CleaningSuite.Application.Employees;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Application.Tenants.Commands;
using CleaningSuite.Domain.Tenants;
using CleaningSuite.Infrastructure.Auth;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace CleaningSuite.Tests.Auth;

public class ApiAudienceTests
{
    [Theory]
    [InlineData("cleaning-suite-api", 200)]
    [InlineData("account|cleaning-suite-api", 200)]
    [InlineData("account", 401)]
    [InlineData("cleaning-suite-web", 401)]
    [InlineData("cleaning-suite-platform-api", 401)]
    [InlineData("CLEANING-SUITE-API", 401)]
    [InlineData("cleaning-suite-api/", 401)]
    [InlineData("", 401)]
    [InlineData(null, 401)]
    public async Task TenantRequiresExactApiAudience(string? audience, int expected)
    {
        using var host = new Host();
        Assert.Equal((HttpStatusCode)expected, (await host.Get("/api/v1/me", host.Token("tenant-a", audience))).StatusCode);
    }

    [Theory]
    [InlineData("expired")][InlineData("not-yet-valid")][InlineData("no-expiry")]
    [InlineData("bad-signature")][InlineData("unsigned")][InlineData("foreign-issuer")]
    [InlineData("invalid-realm")][InlineData("tampered")]
    public async Task AudienceDoesNotBypassSignatureIssuerOrLifetime(string defect)
    {
        using var host = new Host();
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Get("/api/v1/me", host.Token("tenant-a", "cleaning-suite-api", defect: defect))).StatusCode);
    }

    [Theory]
    [InlineData("master", "cleaning-suite-platform-api", PlatformAuthorization.Permission, 200)]
    [InlineData("master", "account|cleaning-suite-platform-api", PlatformAuthorization.Permission, 200)]
    [InlineData("master", "cleaning-suite-platform-api", "admin", 403)]
    [InlineData("master", "cleaning-suite-platform-api", "employee", 403)]
    [InlineData("master", "cleaning-suite-platform-api", "", 403)]
    [InlineData("master", "cleaning-suite-platform-api", "malformed", 403)]
    [InlineData("master", "cleaning-suite-platform-api", "PLATFORM.TENANTS.MANAGE", 403)]
    [InlineData("master", "cleaning-suite-api", PlatformAuthorization.Permission, 401)]
    [InlineData("master", "account", PlatformAuthorization.Permission, 401)]
    [InlineData("master", null, PlatformAuthorization.Permission, 401)]
    [InlineData("tenant-a", "cleaning-suite-api", "admin", 403)]
    [InlineData("tenant-a", "cleaning-suite-api|cleaning-suite-platform-api", PlatformAuthorization.Permission, 403)]
    [InlineData("tenant-a", "cleaning-suite-platform-api", PlatformAuthorization.Permission, 401)]
    [InlineData(null, null, null, 401)]
    public async Task EverySystemOperationRequiresPlatformIssuerAudienceAndPermission(string? realm, string? audience, string? role, int expected)
    {
        using var host = new Host();
        var token = realm is null ? null : host.Token(realm, audience, role!);
        foreach (var (method, path, body, success) in new[] {
            ("GET", "/api/v1/system/tenants", (string?)null, 200),
            ("POST", "/api/v1/system/tenants", "{\"slug\":\"tenant-new\",\"companyName\":\"Test\",\"ownerEmail\":\"owner@example.invalid\",\"ownerFirstName\":\"Test\",\"ownerLastName\":\"Owner\"}", 201),
            ("POST", "/api/v1/system/tenants/tenant-a/suspend", (string?)null, 204),
            ("POST", "/api/v1/system/tenants/tenant-a/activate", (string?)null, 204),
        })
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            if (token is not null) request.Headers.Authorization = new("Bearer", token);
            if (body is not null) request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
            Assert.Equal((HttpStatusCode)(expected == 200 ? success : expected), (await host.Client.SendAsync(request)).StatusCode);
        }
        Assert.Equal(expected == 200 ? 4 : 0, host.Mediator.Invocations.Count);
        Assert.Equal(4, host.Audit.Messages.Count);
        Assert.All(host.Audit.Messages, message => Assert.Contains(expected == 200 ? "succeeded" : "rejected", message));
        if (token is not null) Assert.All(host.Audit.Messages, message => Assert.DoesNotContain(token, message));
        if (expected == 401) Assert.All(host.Audit.Messages, message => Assert.Contains("unauthenticated", message));
    }

    [Theory]
    [InlineData("admin")][InlineData("employee")][InlineData(PlatformAuthorization.Permission)]
    public async Task PlatformIdentityCannotEnterTenantApisEvenWithRolesBothAudiencesAndActiveMasterRegistration(string role)
    {
        using var host = new Host();
        var token = host.Token("master", "cleaning-suite-api|cleaning-suite-platform-api", role);
        foreach (var path in new[] { "/api/v1/me", "/api/v1/admin/employees", "/api/v1/admin/quality-cycle/templates" })
            Assert.Equal(HttpStatusCode.Forbidden, (await host.Get(path, token)).StatusCode);
        Assert.Empty(host.Employees.Invocations);
    }

    [Theory]
    [InlineData("Audience", "")][InlineData("PlatformAudience", "")]
    [InlineData("Audience", "account")][InlineData("PlatformAudience", "cleaning-suite-web")]
    [InlineData("PlatformAudience", "cleaning-suite-api")][InlineData("Audience", " cleaning-suite-api")]
    [InlineData("ProvisionerRealm", "../master")]
    public void InvalidResourceConfigurationFailsClosed(string name, string value)
    {
        var values = Configuration(); values["Auth:Keycloak:" + name] = value;
        Assert.Throws<InvalidOperationException>(() => new KeycloakResourceOptions(new ConfigurationBuilder().AddInMemoryCollection(values).Build()));
    }

    [Fact]
    public async Task ConflictAndHandlerFailureAreAuditedWithoutLoggingTemporaryPasswords()
    {
        using var host = new Host();
        host.Mediator.Setup(x => x.Send(It.IsAny<ProvisionTenantCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ProvisionTenantResult("tenant-new", "tenant-new", "cleaning-suite-web", "do-not-log-password", true));
        var token = host.Token("master", "cleaning-suite-platform-api", PlatformAuthorization.Permission);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/system/tenants") { Content = new StringContent("{\"slug\":\"tenant-new\",\"companyName\":\"Test\",\"ownerEmail\":\"owner@example.invalid\",\"ownerFirstName\":\"Test\",\"ownerLastName\":\"Owner\"}", System.Text.Encoding.UTF8, "application/json") };
        request.Headers.Authorization = new("Bearer", token);
        Assert.Equal(HttpStatusCode.Conflict, (await host.Client.SendAsync(request)).StatusCode);
        Assert.Contains("conflict", Assert.Single(host.Audit.Messages));
        Assert.Contains("tenant tenant-new", Assert.Single(host.Audit.Messages));
        host.Mediator.Setup(x => x.Send(It.IsAny<ListTenantsQuery>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("test failure"));
        Assert.Equal(HttpStatusCode.InternalServerError, (await host.Get("/api/v1/system/tenants", token)).StatusCode);
        Assert.Contains("failed", host.Audit.Messages.Last());
        host.Mediator.Setup(x => x.Send(It.IsAny<ListTenantsQuery>(), It.IsAny<CancellationToken>())).ThrowsAsync(new CleaningSuite.Application.Common.NotFoundException("TenantRegistration", Guid.Empty));
        Assert.Equal(HttpStatusCode.NotFound, (await host.Get("/api/v1/system/tenants", token)).StatusCode);
        Assert.Contains("status 404 outcome failed", host.Audit.Messages.Last());
        Assert.All(host.Audit.Messages, message => Assert.DoesNotContain("do-not-log-password", message));
    }

    [Fact]
    public async Task PlatformPermissionRequiresAnAuditableSubject()
    {
        using var host = new Host();
        var token = host.Token("master", "cleaning-suite-platform-api", PlatformAuthorization.Permission, "no-subject");
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Get("/api/v1/system/tenants", token)).StatusCode);
        Assert.Empty(host.Mediator.Invocations);
    }

    private static Dictionary<string, string?> Configuration() => new() {
        ["Auth:Keycloak:ServerUrl"] = "https://auth.test", ["Auth:Keycloak:Audience"] = "cleaning-suite-api",
        ["Auth:Keycloak:PlatformAudience"] = "cleaning-suite-platform-api", ["Auth:Keycloak:ProvisionerRealm"] = "master",
        ["Logging:LogLevel:Microsoft.AspNetCore.Authentication"] = "Information"
    };

    private sealed class Host : IDisposable
    {
        private readonly RSA _rsa = RSA.Create(2048);
        private readonly RsaSecurityKey _key;
        private readonly WebApplicationFactory<Program> _factory;
        public Mock<IMediator> Mediator { get; } = new();
        public Mock<IEmployeeRepository> Employees { get; } = new();
        public AuditLogger Audit { get; } = new();
        public HttpClient Client { get; }

        public Host()
        {
            _key = new RsaSecurityKey(_rsa) { KeyId = "test-key" };
            var parameters = _rsa.ExportParameters(false);
            var publicKey = new { kty = "RSA", kid = _key.KeyId, use = "sig", alg = "RS256",
                n = Base64UrlEncoder.Encode(parameters.Modulus!), e = Base64UrlEncoder.Encode(parameters.Exponent!) };
            var registry = new Mock<ITenantRegistry>();
            registry.Setup(x => x.GetBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string slug, CancellationToken _) => new TenantRegistration { Slug = slug, Status = TenantRegistration.StatusActive });
            Mediator.Setup(x => x.Send(It.IsAny<ListTenantsQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<TenantRegistration>());
            Mediator.Setup(x => x.Send(It.IsAny<ProvisionTenantCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ProvisionTenantResult("tenant-new", "tenant-new", "cleaning-suite-web", "do-not-log-password", false));
            Mediator.Setup(x => x.Send(It.IsAny<SetTenantStatusCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(Unit.Value);
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => {
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(Configuration()));
                builder.ConfigureTestServices(services => {
                    foreach (var descriptor in services.Where(x => x.ImplementationType == typeof(MartenSchemaInitializer) || x.ImplementationType == typeof(QualityCycleWorker)).ToList()) services.Remove(descriptor);
                    services.RemoveAll<ITenantRegistry>(); services.AddSingleton(registry.Object);
                    services.RemoveAll<IEmployeeRepository>(); services.AddSingleton(Employees.Object);
                    services.RemoveAll<IMediator>(); services.AddSingleton(Mediator.Object);
                    services.AddHttpClient("KeycloakSigningKeys").ConfigurePrimaryHttpMessageHandler(() => new Jwks(JsonSerializer.Serialize(new { keys = new[] { publicKey } })));
                    services.AddSingleton<ILogger<PlatformAuditMiddleware>>(Audit);
                });
            });
            Client = _factory.CreateClient();
        }

        public string Token(string realm, string? audiences, string role = "admin", string? defect = null)
        {
            var issuer = defect == "foreign-issuer" ? "https://foreign.test/realms/tenant-a" : "https://auth.test/realms/" + (defect == "invalid-realm" ? "../tenant-a" : realm);
            var claims = new List<Claim> { new Claim("realm_access", role == "malformed" ? "{\"roles\":false}" : JsonSerializer.Serialize(new { roles = new[] { role } })) };
            if (defect != "no-subject") claims.Add(new Claim("sub", "test-sub"));
            var payload = new JwtPayload(issuer, null, claims, DateTime.UtcNow.AddMinutes(defect == "not-yet-valid" ? 20 : -20), defect == "no-expiry" ? null : DateTime.UtcNow.AddMinutes(defect == "expired" ? -10 : 20));
            if (audiences is not null) payload["aud"] = audiences.Contains('|') ? audiences.Split('|') : audiences;
            using var wrong = RSA.Create(2048);
            var credentials = defect == "unsigned" ? null : new SigningCredentials(defect == "bad-signature" ? new RsaSecurityKey(wrong) { KeyId = _key.KeyId } : _key, SecurityAlgorithms.RsaSha256);
            var jwt = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(new JwtHeader(credentials), payload));
            if (defect == "tampered") { var pieces = jwt.Split('.'); pieces[1] = Base64UrlEncoder.Encode(System.Text.Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(pieces[1])).Replace("test-sub", "tampered")); jwt = string.Join('.', pieces); }
            return jwt;
        }
        public Task<HttpResponseMessage> Get(string path, string? token) { var request = new HttpRequestMessage(HttpMethod.Get, path); if (token is not null) request.Headers.Authorization = new("Bearer", token); return Client.SendAsync(request); }
        public void Dispose() { Client.Dispose(); _factory.Dispose(); _rsa.Dispose(); }
    }
    private sealed class Jwks(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
    }
    private sealed class AuditLogger : ILogger<PlatformAuditMiddleware>
    {
        public List<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
