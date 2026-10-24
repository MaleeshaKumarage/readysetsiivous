using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Domain.Tenants;
using CleaningSuite.Infrastructure.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace CleaningSuite.Tests.Auth;

public sealed class BoundedSigningKeysTests
{
    [Theory]
    [InlineData("https://foreign.test/realms/tenant-a")]
    [InlineData("http://auth.test/realms/tenant-a")]
    [InlineData("https://auth.test/realms/../tenant-a")]
    [InlineData("https://auth.test/realms/tenant-a?query")]
    [InlineData("https://auth.test/realms/tenant-a#fragment")]
    [InlineData("https://auth.test/realms/tenant%2Da")]
    [InlineData("https://auth.test/realms/tenant-a/")]
    [InlineData("https://auth.test/realms/tenant-a\n")]
    public async Task InvalidIssuerDoesNoRegistryOrHttpWork(string issuer)
    {
        using var fixture = new Fixture();
        var result = await fixture.Validate(fixture.Token(issuer));
        Assert.False(result.IsValid);
        Assert.Equal(0, fixture.Lookups);
        Assert.Equal(0, fixture.Requests);
    }

    [Fact]
    public async Task MalformedAndOversizedBearerDoesNoDiscovery()
    {
        using var fixture = new Fixture();
        foreach (var token in new[] { "not-a-jwt", new string('x', KeycloakTokenHandler.MaximumBearerCharacters + 1) })
            Assert.False((await fixture.Validate(token)).IsValid);
        Assert.Equal(0, fixture.Lookups); Assert.Equal(0, fixture.Requests);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("different", false)]
    [InlineData("tenant-a", true)]
    [InlineData("", true)]
    public async Task RegisteredCanonicalRealmRequiredAndLegacyBlankRealmSupported(string? registeredRealm, bool valid)
    {
        using var fixture = new Fixture();
        fixture.Registration = registeredRealm is null ? null : new TenantRegistration { Slug = "tenant-a", KeycloakRealm = registeredRealm };
        Assert.Equal(valid, (await fixture.Validate(fixture.Token())).IsValid);
        Assert.Equal(1, fixture.Lookups); Assert.Equal(valid ? 1 : 0, fixture.Requests);
        await fixture.Validate(fixture.Token());
        Assert.Equal(1, fixture.Lookups);
    }

    [Fact]
    public async Task PlatformRealmIsExplicitAndStillRequiresItsOwnAudience()
    {
        using var fixture = new Fixture(); fixture.Registration = null;
        Assert.True((await fixture.Validate(fixture.Token("https://auth.test/realms/master", "platform-api"))).IsValid);
        Assert.False((await fixture.Validate(fixture.Token("https://auth.test/realms/master", "tenant-api"))).IsValid);
        Assert.Equal(0, fixture.Lookups); Assert.Equal(1, fixture.Requests);
    }

    [Fact]
    public async Task CryptographicAudienceAndLifetimeChecksRemainRequired()
    {
        using var fixture = new Fixture();
        Assert.False((await fixture.Validate(fixture.Token(audience: "other"))).IsValid);
        Assert.False((await fixture.Validate(fixture.Token(expired: true))).IsValid);
        using var wrong = RSA.Create(2048);
        Assert.False((await fixture.Validate(fixture.Token(key: new RsaSecurityKey(wrong) { KeyId = "key-one" }))).IsValid);
        Assert.True((await fixture.Validate(fixture.Token())).IsValid);
    }

    [Fact]
    public async Task SameRealmSingleFlightAndCancelledCallerDoesNotCancelOthers()
    {
        using var fixture = new Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Respond = async ct => { entered.TrySetResult(); await release.Task.WaitAsync(ct); return fixture.Keys(); };
        using var cancellation = new CancellationTokenSource();
        var first = fixture.Resolver.ResolveAsync(Fixture.Issuer, "key-one", cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = fixture.Resolver.ResolveAsync(Fixture.Issuer, "key-one", CancellationToken.None);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        release.SetResult(); Assert.NotEmpty(await second);
        Assert.Equal(1, fixture.Lookups); Assert.Equal(1, fixture.Requests);
    }

    [Fact]
    public async Task DistinctRealmsCannotQueueUnboundedRegistryOrHttpWork()
    {
        using var fixture = new Fixture();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Registry.Setup(x => x.GetBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string realm, CancellationToken ct) => { Interlocked.Increment(ref fixture.Lookups); await release.Task.WaitAsync(ct); return new TenantRegistration { Slug = realm }; });
        var flights = Enumerable.Range(0, KeycloakSigningKeysResolver.MaximumConcurrentFetches)
            .Select(i => fixture.Resolver.ResolveAsync("https://auth.test/realms/tenant-" + i, "key-one", CancellationToken.None)).ToArray();
        await Assert.ThrowsAsync<SecurityTokenInvalidSigningKeyException>(() => fixture.Resolver.ResolveAsync("https://auth.test/realms/excess", "key-one", CancellationToken.None));
        Assert.Equal(KeycloakSigningKeysResolver.MaximumConcurrentFetches, fixture.Lookups);
        Assert.Equal(0, fixture.Requests);
        release.SetResult(); await Task.WhenAll(flights);
    }

    [Fact]
    public async Task FailedDiscoveryIsNegativelyCachedThenRecovers()
    {
        using var fixture = new Fixture();
        fixture.Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        Assert.False((await fixture.Validate(fixture.Token())).IsValid);
        Assert.False((await fixture.Validate(fixture.Token())).IsValid);
        Assert.Equal(1, fixture.Requests);
        fixture.Clock.Advance(TimeSpan.FromSeconds(6)); fixture.Respond = _ => Task.FromResult(fixture.Keys());
        Assert.True((await fixture.Validate(fixture.Token())).IsValid); Assert.Equal(2, fixture.Requests);
    }

    [Fact]
    public async Task UnknownKidHasOneRefreshAfterCooldownAndSupportsRotation()
    {
        using var fixture = new Fixture();
        Assert.True((await fixture.Validate(fixture.Token())).IsValid);
        using var nextRsa = RSA.Create(2048); var next = new RsaSecurityKey(nextRsa) { KeyId = "key-two" };
        fixture.Respond = _ => Task.FromResult(fixture.Keys(nextRsa, "key-two"));
        Assert.False((await fixture.Validate(fixture.Token(key: next))).IsValid); Assert.Equal(1, fixture.Requests);
        fixture.Clock.Advance(TimeSpan.FromSeconds(31));
        Assert.True((await fixture.Validate(fixture.Token(key: next))).IsValid); Assert.Equal(2, fixture.Requests);
        for (var i = 0; i < 10; i++) await fixture.Resolver.ResolveAsync(Fixture.Issuer, "unknown-" + i, CancellationToken.None);
        Assert.Equal(2, fixture.Requests);
    }

    [Fact]
    public async Task BodyLimitAppliesWithoutContentLengthAndOversizedResponsesAreCachedFailures()
    {
        using var fixture = new Fixture();
        fixture.Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StreamContent(new NonSeekableStream(new byte[KeycloakSigningKeysResolver.MaximumJwksBytes + 1])) });
        Assert.False((await fixture.Validate(fixture.Token())).IsValid);
        Assert.False((await fixture.Validate(fixture.Token())).IsValid);
        Assert.Equal(1, fixture.Requests);
    }

    [Fact]
    public async Task SlowDiscoveryTimesOutAndReleasesAdmission()
    {
        using var fixture = new Fixture();
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Respond = async ct =>
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            finally { if (ct.IsCancellationRequested) cancelled.TrySetResult(); }
            return fixture.Keys();
        };
        Assert.False((await fixture.Validate(fixture.Token()).WaitAsync(TimeSpan.FromSeconds(8))).IsValid);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        fixture.Clock.Advance(TimeSpan.FromSeconds(6));
        fixture.Respond = _ => Task.FromResult(fixture.Keys());
        Assert.True((await fixture.Validate(fixture.Token())).IsValid);
    }

    [Fact]
    public async Task ExcessiveSigningKeyCountFailsClosedAndIsNegativelyCached()
    {
        using var fixture = new Fixture();
        using var rsa = RSA.Create(2048);
        var p = rsa.ExportParameters(false);
        var keys = Enumerable.Range(0, 33).Select(i => new {
            kty = "RSA", kid = i == 0 ? "key-one" : "key-" + i, use = "sig", alg = "RS256",
            n = Base64UrlEncoder.Encode(p.Modulus!), e = Base64UrlEncoder.Encode(p.Exponent!) });
        fixture.Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent(JsonSerializer.Serialize(new { keys })) });
        Assert.Empty(await fixture.Resolver.ResolveAsync(Fixture.Issuer, "key-one", CancellationToken.None));
        Assert.Empty(await fixture.Resolver.ResolveAsync(Fixture.Issuer, "key-one", CancellationToken.None));
        Assert.Equal(1, fixture.Requests);
    }

    [Fact]
    public async Task HardCacheCapacityIncludesNegativeEntries()
    {
        using var fixture = new Fixture(); fixture.Registration = null;
        for (var i = 0; i < KeycloakSigningKeysResolver.MaximumRealms; i++)
            await fixture.Resolver.ResolveAsync("https://auth.test/realms/unknown-" + i, "key-one", CancellationToken.None);
        await Assert.ThrowsAsync<SecurityTokenInvalidSigningKeyException>(() => fixture.Resolver.ResolveAsync("https://auth.test/realms/overflow", "key-one", CancellationToken.None));
        Assert.Equal(KeycloakSigningKeysResolver.MaximumRealms, fixture.Lookups); Assert.Equal(0, fixture.Requests);
        fixture.Clock.Advance(TimeSpan.FromSeconds(6));
        await fixture.Resolver.ResolveAsync("https://auth.test/realms/overflow", "key-one", CancellationToken.None);
        Assert.Equal(KeycloakSigningKeysResolver.MaximumRealms + 1, fixture.Lookups);
    }

    private sealed class Fixture : IDisposable
    {
        public const string Issuer = "https://auth.test/realms/tenant-a";
        private readonly RSA _rsa = RSA.Create(2048);
        private readonly ServiceProvider _services;
        private readonly HttpClient _http;
        private readonly KeycloakTokenHandler _handler;
        private readonly TokenValidationParameters _parameters;
        public ManualTime Clock { get; } = new();
        public Mock<ITenantRegistry> Registry { get; } = new();
        public TenantRegistration? Registration = new() { Slug = "tenant-a" };
        public int Lookups;
        public int Requests;
        public Func<CancellationToken, Task<HttpResponseMessage>> Respond;
        public KeycloakSigningKeysResolver Resolver { get; }
        public Fixture()
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
                ["Auth:Keycloak:ServerUrl"] = "https://auth.test", ["Auth:Keycloak:AdminServerUrl"] = "http://internal.test",
                ["Auth:Keycloak:Audience"] = "tenant-api", ["Auth:Keycloak:PlatformAudience"] = "platform-api" }).Build();
            Registry.Setup(x => x.GetBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, CancellationToken _) => { Interlocked.Increment(ref Lookups); return Registration; });
            _services = new ServiceCollection().AddSingleton(Registry.Object).BuildServiceProvider();
            Respond = _ => Task.FromResult(Keys());
            _http = new HttpClient(new Stub(async (request, ct) => {
                Assert.Equal("http", request.RequestUri!.Scheme); Assert.Equal("internal.test", request.RequestUri.Host);
                Interlocked.Increment(ref Requests); return await Respond(ct); }));
            var clients = new Mock<IHttpClientFactory>(); clients.Setup(x => x.CreateClient("KeycloakSigningKeys")).Returns(_http);
            Resolver = new KeycloakSigningKeysResolver(config, clients.Object, _services.GetRequiredService<IServiceScopeFactory>(), new(config), Clock);
            _handler = new(Resolver, new HttpContextAccessor());
            _parameters = new TokenValidationParameters { ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true,
                ValidateIssuerSigningKey = true, RequireSignedTokens = true,
                IssuerValidator = (issuer, _, _) => { KeycloakJwt.RealmFromIssuer(issuer, "https://auth.test"); return issuer; },
                AudienceValidator = (audiences, token, _) => audiences.Contains(token.Issuer.EndsWith("/master") ? "platform-api" : "tenant-api") };
        }
        public Task<TokenValidationResult> Validate(string token) => _handler.ValidateTokenAsync(token, _parameters);
        public string Token(string issuer = Issuer, string audience = "tenant-api", SecurityKey? key = null, bool expired = false) =>
            new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(issuer, audience, [new Claim("sub", "synthetic")],
                DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddMinutes(expired ? -20 : 20),
                new SigningCredentials(key ?? new RsaSecurityKey(_rsa) { KeyId = "key-one" }, SecurityAlgorithms.RsaSha256)));
        public HttpResponseMessage Keys(RSA? rsa = null, string kid = "key-one")
        {
            var p = (rsa ?? _rsa).ExportParameters(false);
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { keys = new[] {
                new { kty = "RSA", kid, use = "sig", alg = "RS256", n = Base64UrlEncoder.Encode(p.Modulus!), e = Base64UrlEncoder.Encode(p.Exponent!) } } })) };
        }
        public void Dispose() { _http.Dispose(); _services.Dispose(); _rsa.Dispose(); }
    }
    public sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan value) => _now += value;
    }
    private sealed class Stub(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }
    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
