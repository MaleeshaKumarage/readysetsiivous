using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace CleaningSuite.Infrastructure.Auth;

/// <summary>
/// Multi-realm Keycloak JWT validation. Issuer is {server}/realms/{realm};
/// signing keys are fetched per-realm from the OIDC certs endpoint and cached,
/// so new tenant realms work without an API restart.
/// </summary>
public static class KeycloakJwt
{
    private static readonly Regex RealmFormat = new(
        @"^[a-z0-9][a-z0-9-]{2,62}$", RegexOptions.Compiled);

    public static IServiceCollection AddKeycloakAuth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var serverUrl = configuration["Auth:Keycloak:ServerUrl"]?.TrimEnd('/')
            ?? throw new InvalidOperationException("Auth:Keycloak:ServerUrl missing");
        var audiences = configuration.GetSection("Auth:Keycloak:Audiences").Get<string[]>()
            ?? (configuration["Auth:Keycloak:Audience"] is { } single
                ? [single]
                : throw new InvalidOperationException("Auth:Keycloak:Audience missing"));

        services.AddSingleton<KeycloakSigningKeysResolver>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<KeycloakSigningKeysResolver>((options, resolver) =>
            {
                // Keep raw claim names ("iss", "realm_access") for issuer/realm parsing.
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    // Master admin-cli tokens carry no aud claim. Issuer + signature
                    // + registry check already gate access; add an aud whitelist here
                    // once all real clients (provisioner, SPA) consistently send aud.
                    ValidateAudience = false,
                    ValidAudiences = audiences,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerValidator = (issuer, securityToken, validationParameters) =>
                        ValidateIssuer(issuer, serverUrl),
                    IssuerSigningKeyResolver = (token, securityToken, kid, validationParameters) =>
                        resolver.Resolve(token, kid),
                };
            });

        return services;
    }

    /// <summary>
    /// Issuer must be {server}/realms/{realm} with a realm-shaped last segment.
    /// Scheme http/https both accepted: behind a TLS-terminating tunnel Keycloak
    /// may advertise the http issuer.
    /// </summary>
    private static string ValidateIssuer(string issuer, string serverUrl)
    {
        var realm = RealmFromIssuer(issuer, serverUrl);
        if (!RealmFormat.IsMatch(realm))
            throw new SecurityTokenInvalidIssuerException($"Invalid realm name in issuer: {realm}");

        return issuer;
    }

    /// <summary>Extracts the realm from a token's issuer claim, tolerating http/https scheme difference.</summary>
    public static string RealmFromIssuer(string issuer, string serverUrl)
    {
        var serverWithoutScheme = StripScheme(serverUrl);
        var issuerWithoutScheme = StripScheme(issuer);

        var prefix = $"{serverWithoutScheme}/realms/";
        if (!issuerWithoutScheme.StartsWith(prefix, StringComparison.Ordinal))
            throw new SecurityTokenInvalidIssuerException(
                $"Issuer {issuer} is outside configured Keycloak server {serverUrl}");

        return issuerWithoutScheme[prefix.Length..];
    }

    private static string StripScheme(string url)
    {
        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return url["https://".Length..];
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return url["http://".Length..];
        return url;
    }
}

/// <summary>Fetches and caches per-realm signing keys from Keycloak JWKS endpoints.</summary>
public class KeycloakSigningKeysResolver
{
    private static readonly HttpClient Http = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly string _serverUrl;

    public KeycloakSigningKeysResolver(IConfiguration configuration)
    {
        _serverUrl = configuration["Auth:Keycloak:ServerUrl"]!.TrimEnd('/');
    }

    public IEnumerable<SecurityKey> Resolve(string token, string kid)
    {
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        var issuer = jwt.Issuer;
        var realm = KeycloakJwt.RealmFromIssuer(issuer, _serverUrl);

        if (_cache.TryGetValue<JsonWebKeySet>(realm, out var cached) && cached is not null)
            return cached.GetSigningKeys();

        try
        {
            var response = Http.GetAsync(
                $"{issuer}/protocol/openid-connect/certs").GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var keys = new JsonWebKeySet(json);
            _cache.Set(realm, keys, TimeSpan.FromMinutes(15));
            return keys.GetSigningKeys();
        }
        catch
        {
            // Keycloak may be briefly down; fail the request rather than accept a bad key.
            throw new SecurityTokenInvalidSigningKeyException(
                $"Could not fetch signing keys for realm {realm}");
        }
    }
}
