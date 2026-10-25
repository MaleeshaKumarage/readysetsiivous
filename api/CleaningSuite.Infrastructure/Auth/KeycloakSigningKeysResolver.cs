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
    public const int MaximumNegativeRealms = 256;
    public const int MaximumConcurrentFetches = 8;
    public const int MaximumJwksBytes = 65536;
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _known = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Entry> _negative = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<SecurityKey[]>> _flights = new(StringComparer.Ordinal);
    private readonly int _maximumKnownRealms;
    private long _accessSequence;
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
        public long LastAccess;
    }

    public KeycloakSigningKeysResolver(IConfiguration configuration, IHttpClientFactory clients,
        IServiceScopeFactory scopes, KeycloakResourceOptions resources, TimeProvider? timeProvider = null)
    {
        _scopes = scopes; _http = clients.CreateClient("KeycloakSigningKeys");
        _serverUrl = configuration["Auth:Keycloak:ServerUrl"]!;
        _transportUrl = KeycloakJwt.ServerUri(configuration["Auth:Keycloak:AdminServerUrl"] ?? _serverUrl).AbsoluteUri.TrimEnd('/');
        _platformRealm = resources.ProvisionerRealm;
        _time = timeProvider ?? TimeProvider.System;
        var maximum = configuration["Auth:Keycloak:SigningKeys:MaximumKnownRealms"];
        if (maximum is not null && (!int.TryParse(maximum, out var parsed) || parsed is < 1 or > 4096))
            throw new InvalidOperationException("MaximumKnownRealms must be between 1 and 4096");
        _maximumKnownRealms = maximum is null ? MaximumRealms : int.Parse(maximum);
    }

    public async Task<SecurityKey[]> ResolveAsync(string issuer, string kid, CancellationToken cancellationToken)
    {
        var realm = KeycloakJwt.RealmFromIssuer(issuer, _serverUrl);
        cancellationToken.ThrowIfCancellationRequested();
        Task<SecurityKey[]> flight;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            if (_known.TryGetValue(realm, out var entry))
            {
                entry.LastAccess = ++_accessSequence;
                if (entry.Expires > now && (entry.Keys.Any(k => k.KeyId == kid) || entry.RefreshAfter > now))
                    return entry.Keys;
            }
            if (_negative.TryGetValue(realm, out var rejected))
            {
                if (rejected.Expires > now)
                {
                    rejected.LastAccess = ++_accessSequence;
                    return [];
                }
                _negative.Remove(realm);
            }
            if (_flights.TryGetValue(realm, out var existing)) flight = existing;
            else
            {
                // Never queue arbitrary realm lookups behind a semaphore. Admission bounds registry AND HTTP work.
                if (!_admission.Wait(0)) throw Unavailable();
                var completion = new TaskCompletionSource<SecurityKey[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                _flights.Add(realm, flight = completion.Task);
                _ = FetchAsync(realm, completion);
            }
        }
        // One disconnected request must not cancel the shared fetch needed by other requests.
        return await flight.WaitAsync(cancellationToken);
    }

    private async Task FetchAsync(string realm, TaskCompletionSource<SecurityKey[]> completion)
    {
        SecurityKey[] keys = [];
        var verifiedRealm = realm == _platformRealm;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            if (realm != _platformRealm)
            {
                using var scope = _scopes.CreateScope();
                var tenant = await scope.ServiceProvider.GetRequiredService<ITenantRegistry>().GetBySlugAsync(realm, timeout.Token);
                if (tenant is null || tenant.Slug != realm || (!string.IsNullOrEmpty(tenant.KeycloakRealm) && tenant.KeycloakRealm != realm))
                    throw Unavailable();
                verifiedRealm = true;
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
            try
            {
                lock (_gate)
                {
                    var now = _time.GetUtcNow();
                    var entry = new Entry
                    {
                        Keys = keys,
                        Expires = now.Add(keys.Length == 0 ? TimeSpan.FromSeconds(5) : TimeSpan.FromMinutes(15)),
                        RefreshAfter = now.Add(keys.Length == 0 ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(30)),
                        LastAccess = ++_accessSequence
                    };
                    // Unverified realm names can never occupy slots reserved for registered tenants.
                    // Flights live separately, so evicting idle cache entries cannot duplicate a fetch.
                    if (verifiedRealm)
                    {
                        _negative.Remove(realm);
                        // A failed rotation lookup must not erase still-valid known keys. Keep
                        // their original expiry; only retry backoff changes. Never retain keys
                        // when current registry verification failed or their original TTL elapsed.
                        if (keys.Length == 0 && _known.TryGetValue(realm, out var previous)
                            && previous.Keys.Length != 0 && previous.Expires > now)
                        {
                            entry.Keys = previous.Keys;
                            entry.Expires = previous.Expires;
                            entry.RefreshAfter = now.AddSeconds(30);
                        }
                        if (_known.ContainsKey(realm) || MakeRoom(_known, _maximumKnownRealms))
                            _known[realm] = entry;
                        // If every cached realm is refreshing, return these verified keys without caching.
                        // Cache capacity is an optimization, never a reason to reject a successful fetch.
                    }
                    else
                    {
                        _known.Remove(realm);
                        if (MakeRoom(_negative, MaximumNegativeRealms)) _negative[realm] = entry;
                    }
                }
            }
            catch (Exception)
            {
                // A cache bookkeeping failure must not strand shared callers or consume a permit forever.
                keys = [];
                lock (_gate)
                {
                    _known.Remove(realm);
                    _negative.Remove(realm);
                }
            }
            finally
            {
                lock (_gate) _flights.Remove(realm);
                _admission.Release();
                completion.TrySetResult(keys);
            }
        }
    }
    // Called only under _gate. Do not evict a realm with an outstanding shared fetch.
    private bool MakeRoom(Dictionary<string, Entry> entries, int maximum)
    {
        if (entries.Count < maximum) return true;
        var idle = entries.Where(x => !_flights.ContainsKey(x.Key)).OrderBy(x => x.Value.LastAccess).FirstOrDefault();
        if (idle.Key is null) return false;
        entries.Remove(idle.Key);
        return true;
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
