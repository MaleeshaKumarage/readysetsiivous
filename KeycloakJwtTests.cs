using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Tenants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit;

public class KeycloakJwtTests : IClassFixture<WebApplicationFactory<CleaningSuite.Api.Startup>>
{
    private readonly WebApplicationFactory<CleaningSuite.Api.Startup> _factory;

    public KeycloakJwtTests(WebApplicationFactory<CleaningSuite.Api.Startup> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Remove the real registration first so the mock is the only one resolved.
                services.RemoveAll<IKeycloakProvisioner>();
                services.AddSingleton<IKeycloakProvisioner, MockKeycloakProvisioner>();
            });
        });
    }

    [Fact]
    public async Task HealthEndpoint_IsAnonymous()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/healthz");
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public void AuthenticationMiddleware_IsEnabled_WithJwtBearerScheme()
    {
        // Guard against the test passing trivially: the host must actually wire up
        // authentication and expose a JWT bearer scheme as the default challenge.
        using var scope = _factory.Services.CreateScope();
        var schemeProvider = scope.ServiceProvider.GetRequiredService<IAuthenticationSchemeProvider>();

        var defaultChallenge = schemeProvider.GetDefaultChallengeSchemeAsync().GetAwaiter().GetResult();
        Assert.NotNull(defaultChallenge);
        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, defaultChallenge!.Name);

        var scheme = schemeProvider.GetSchemeAsync(JwtBearerDefaults.AuthenticationScheme)
            .GetAwaiter().GetResult();
        Assert.NotNull(scheme);
        Assert.Equal(typeof(JwtBearerHandler), scheme!.HandlerType);

        var options = scope.ServiceProvider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        Assert.False(string.IsNullOrWhiteSpace(options.Authority));
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        // A real challenge from the bearer handler advertises the scheme.
        Assert.Contains(
            JwtBearerDefaults.AuthenticationScheme,
            response.Headers.WwwAuthenticate.ToString(),
            System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithInvalidBearerToken_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            JwtBearerDefaults.AuthenticationScheme, "not-a-valid-jwt");
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

internal sealed class MockKeycloakProvisioner : IKeycloakProvisioner
{
    public Task<TenantRegistration> ProvisionTenantAsync(string tenantId, CancellationToken ct = default)
        => Task.FromResult(new TenantRegistration());

    public Task<TenantRegistration> UpdateTenantAsync(string tenantId, TenantRegistration registration, CancellationToken ct = default)
        => Task.FromResult(registration);

    public Task DeleteTenantAsync(string tenantId, CancellationToken ct = default)
        => Task.CompletedTask;
}
