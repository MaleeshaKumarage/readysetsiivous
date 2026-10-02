using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
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
                services.AddSingleton<IKeycloakProvisioner, MockKeycloakProvisioner>();
            });
        });
    }

    [Fact]
    public async Task Test_KeycloakJwtValidation()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/healthz");
        response.EnsureSuccessStatusCode();
    }
}

public class MockKeycloakProvisioner : IKeycloakProvisioner
{
    public Task<TenantRegistration> ProvisionTenantAsync(string tenantId, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public Task<TenantRegistration> UpdateTenantAsync(string tenantId, TenantRegistration registration, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public Task DeleteTenantAsync(string tenantId, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }
}
