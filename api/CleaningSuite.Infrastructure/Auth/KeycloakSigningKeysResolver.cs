using System.Text;
using CleaningSuite.Application.Tenants;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CleaningSuite.Infrastructure.Auth;

/// <summary>All untrusted discovery state, including negative entries and flights, has a hard bound.</summary>
public sealed class KeycloakSigningKeysResolver
{
    public const int MaximumRealms = 256;
    public const int MaximumConcurrentFetches = 8;
    public const int MaximumJwksBytes = 65536;
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _admission = new(MaximumConcurrentFetches);
    private readonly IServiceScopeFactory _scopes;
    private readonly HttpClient _http;
    private readonly string _serverUrl;
    private readonly string _transportUrl;
    private readonly string _platformRealm;
    private readonly TimeProvider _time;
    private sealed class Entry
    {
        public SecurityKey[] Keys = [];
        public DateTimeOffset Expires;
        public DateTimeOffset RefreshAfter;
        public Task<SecurityKey[]>? Flight;
    }

    public KeycloakSigningKeysResolver(IConfiguration configuration, IHttpClientFactory clients,
        IServiceScopeFactory scopes, KeycloakResourceOptions resources, TimeProvider? timeProvider = null)
    {
        _scopes = scopes; _http = clients.CreateClient("KeycloakSigningKeys");
        _serverUrl = configuration["Auth:Keycloak:ServerUrl"]!;
        _transportUrl = KeycloakJwt.ServerUri(configuration["Auth:Keycloak:AdminServerUrl"] ?? _serverUrl).AbsoluteUri.TrimEnd('/');
        _platformRealm = resources.ProvisionerRealm;
        _time = timeProvider ?? TimeProvider.System;
    }

    public async Task<SecurityKey[]> ResolveAsync(string issuer, string kid, CancellationToken cancellationToken)
    {
        var realm = KeycloakJwt.RealmFromIssuer(issuer, _serverUrl);
        cancellationToken.ThrowIfCancellationRequested();
        Task<SecurityKey[]> flight;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            if (!_entries.TryGetValue(realm, out var entry))
            {
                foreach (var stale in _entries.Where(x => x.Value.Flight is null && x.Value.Expires <= now).Select(x => x.Key).ToArray())
                    _entries.Remove(stale);
                if (_entries.Count >= MaximumRealms) throw Unavailable();
                _entries.Add(realm, entry = new Entry());
            }
            if (entry.Flight is not null) flight = entry.Flight;
            else if (entry.Expires > now && (entry.Keys.Any(k => k.KeyId == kid) || entry.RefreshAfter > now))
                return entry.Keys;
            else
            {
                // Never queue arbitrary realm lookups behind a semaphore. Admission bounds registry AND HTTP work.
                if (!_admission.Wait(0)) throw Unavailable();
                var completion = new TaskCompletionSource<SecurityKey[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                entry.Flight = flight = completion.Task;
                _ = FetchAsync(realm, entry, completion);
            }
        }
        // One disconnected request must not cancel the shared fetch needed by other requests.
        return await flight.WaitAsync(cancellationToken);
    }

    private async Task FetchAsync(string realm, Entry entry, TaskCompletionSource<SecurityKey[]> completion)
    {
        SecurityKey[] keys = [];
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            if (realm != _platformRealm)
            {
                using var scope = _scopes.CreateScope();
                var tenant = await scope.ServiceProvider.GetRequiredService<ITenantRegistry>().GetBySlugAsync(realm, timeout.Token);
                if (tenant is null || tenant.Slug != realm || (!string.IsNullOrEmpty(tenant.KeycloakRealm) && tenant.KeycloakRealm != realm))
                    throw Unavailable();
            }
            using var response = await _http.GetAsync($"{_transportUrl}/realms/{realm}/protocol/openid-connect/certs", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaximumJwksBytes) throw Unavailable();
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var body = new MemoryStream();
            var buffer = new byte[4096];
            int read;
            while ((read = await stream.ReadAsync(buffer, timeout.Token)) != 0)
            {
                if (body.Length + read > MaximumJwksBytes) throw Unavailable();
                body.Write(buffer, 0, read);
            }
            var parsedKeys = new JsonWebKeySet(Encoding.UTF8.GetString(body.ToArray())).GetSigningKeys().ToArray();
            if (parsedKeys.Length is 0 or > 32) throw Unavailable();
            keys = parsedKeys;
        }
        catch (Exception) { /* Negative cache; never disclose provider responses or token data. */ }
        finally
        {
            lock (_gate)
            {
                entry.Keys = keys;
                var now = _time.GetUtcNow();
                entry.Expires = now.Add(keys.Length == 0 ? TimeSpan.FromSeconds(5) : TimeSpan.FromMinutes(15));
                entry.RefreshAfter = now.Add(keys.Length == 0 ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(30));
                entry.Flight = null;
                completion.TrySetResult(keys);
            }
            _admission.Release();
        }
    }
    private static SecurityTokenInvalidSigningKeyException Unavailable() => new("Signing keys unavailable");
}

public sealed class KeycloakTokenHandler(KeycloakSigningKeysResolver resolver, IHttpContextAccessor contexts) : TokenHandler
{
    public const int MaximumBearerCharacters = 16384;
    private readonly JsonWebTokenHandler _handler = new() { MapInboundClaims = false, MaximumTokenSizeInBytes = MaximumBearerCharacters };
    public override async Task<TokenValidationResult> ValidateTokenAsync(string token, TokenValidationParameters validationParameters)
    {
        try
        {
            if (string.IsNullOrEmpty(token) || token.Length > MaximumBearerCharacters || !_handler.CanReadToken(token))
                throw new SecurityTokenException("Invalid bearer token");
            var jwt = _handler.ReadJsonWebToken(token);
            if (string.IsNullOrEmpty(jwt.Kid) || jwt.Kid.Length > 256) throw new SecurityTokenException("Invalid signing key identifier");
            var parameters = validationParameters.Clone();
            parameters.IssuerSigningKeys = await resolver.ResolveAsync(jwt.Issuer, jwt.Kid, contexts.HttpContext?.RequestAborted ?? CancellationToken.None);
            return await _handler.ValidateTokenAsync(token, parameters);
        }
        catch (Exception exception)
        {
            return new TokenValidationResult { IsValid = false, Exception = exception };
        }
    }
}
