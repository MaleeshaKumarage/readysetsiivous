using System.Net;
using System.Net.Http.Json;
using CleaningSuite.Infrastructure.Keycloak;

namespace CleaningSuite.Tests.Auth;

public class KeycloakTokenCacheTests
{
    [Theory]
    [InlineData(60)]
    [InlineData(300)]
    public async Task ReusesShortLivedServiceTokenBeforeRefreshDeadline(int lifetime)
    {
        var transport = new TokenTransport(lifetime);
        var client = new KeycloakAdminClient(new HttpClient(transport), "https://auth.invalid", "tenant-a", "runtime", "synthetic");
        Assert.Equal(await client.GetAdminTokenAsync(), await client.GetAdminTokenAsync());
        Assert.Equal(1, transport.Requests);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task DoesNotCacheAnAlreadyExpiredToken(int lifetime)
    {
        var transport = new TokenTransport(lifetime);
        var client = new KeycloakAdminClient(new HttpClient(transport), "https://auth.invalid", "tenant-a", "runtime", "synthetic");
        await client.GetAdminTokenAsync(); await client.GetAdminTokenAsync();
        Assert.Equal(2, transport.Requests);
    }

    private sealed class TokenTransport(int lifetime) : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = JsonContent.Create(new { access_token = "synthetic-token", expires_in = lifetime })
            });
        }
    }
}
