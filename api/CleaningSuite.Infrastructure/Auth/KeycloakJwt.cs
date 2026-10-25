using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace CleaningSuite.Infrastructure.Auth;

public static class KeycloakJwt
{
    private static readonly Regex RealmFormat = new("\\A[a-z0-9][a-z0-9-]{2,62}\\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IServiceCollection AddKeycloakAuth(this IServiceCollection services, IConfiguration configuration)
    {
        _ = ServerUri(configuration["Auth:Keycloak:ServerUrl"]);
        _ = new KeycloakResourceOptions(configuration);
        services.AddSingleton(sp => new KeycloakResourceOptions(sp.GetRequiredService<IConfiguration>()));
        services.AddHttpContextAccessor();
        services.AddHttpClient("KeycloakSigningKeys", client => client.Timeout = TimeSpan.FromSeconds(5))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddSingleton<KeycloakSigningKeysResolver>();
        services.AddSingleton<KeycloakTokenHandler>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<KeycloakTokenHandler, IConfiguration>((options, handler, config) =>
            {
                var serverUrl = config["Auth:Keycloak:ServerUrl"]!;
                var resources = new KeycloakResourceOptions(config);
                options.MapInboundClaims = false;
                options.TokenHandlers.Clear();
                options.TokenHandlers.Add(handler);
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true,
                    ValidateIssuerSigningKey = true, RequireSignedTokens = true,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256, SecurityAlgorithms.RsaSha384, SecurityAlgorithms.RsaSha512],
                    IssuerValidator = (issuer, _, _) => { RealmFromIssuer(issuer, serverUrl); return issuer; },
                    AudienceValidator = (audiences, token, _) => audiences?.Contains(
                        RealmFromIssuer(token.Issuer, serverUrl) == resources.ProvisionerRealm
                            ? resources.PlatformAudience : resources.TenantAudience, StringComparer.Ordinal) == true
                };
            });
        return services;
    }

    internal static Uri ServerUri(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("A valid Keycloak HTTP(S) server URL is required");
        return uri;
    }

    public static string RealmFromIssuer(string issuer, string serverUrl)
    {
        var server = ServerUri(serverUrl);
        if (!Uri.TryCreate(issuer, UriKind.Absolute, out var parsed) || parsed.Scheme != server.Scheme
            || !string.Equals(parsed.Authority, server.Authority, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(parsed.UserInfo) || !string.IsNullOrEmpty(parsed.Query) || !string.IsNullOrEmpty(parsed.Fragment))
            throw new SecurityTokenInvalidIssuerException("Invalid Keycloak issuer");
        // Compare original strings too: URI normalization must not admit escaped or dot segments.
        var prefix = server.GetLeftPart(UriPartial.Authority) + server.AbsolutePath.TrimEnd('/') + "/realms/";
        if (!issuer.StartsWith(prefix, StringComparison.Ordinal))
            throw new SecurityTokenInvalidIssuerException("Invalid Keycloak issuer");
        var realm = issuer[prefix.Length..];
        if (!RealmFormat.IsMatch(realm)) throw new SecurityTokenInvalidIssuerException("Invalid Keycloak realm");
        return realm;
    }
}
