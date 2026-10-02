using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Tenants;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    public async Task ProtectedEndpoint_WithoutToken_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/me");
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
